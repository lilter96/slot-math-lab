"""Run Rider cleanup only on source files changed since this session started."""

import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE_EXTENSIONS = {
    ".cs", ".vb", ".xaml", ".razor", ".cshtml", ".ts", ".tsx",
    ".js", ".jsx", ".css", ".html", ".py", ".sh",
}
BACKEND_EXTENSIONS = {".cs", ".vb", ".xaml", ".razor", ".cshtml"}
BUILD_DIRECTORIES = {"bin", "obj", "node_modules", "dist", "__pycache__"}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def snapshot():
    paths = git("ls-files", "--cached", "--others", "--exclude-standard", "-z")
    result = {}
    for raw in paths.split(b"\0"):
        if not raw:
            continue
        name = raw.decode("utf-8", errors="surrogateescape")
        path = ROOT / name
        if any(part in BUILD_DIRECTORIES for part in Path(name).parts):
            continue
        if path.suffix in SOURCE_EXTENSIONS and path.is_file() and not path.is_symlink():
            result[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


def save(path, state):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(state), encoding="utf-8")
    temporary.replace(path)


def feedback(reason, active):
    if active:
        # Report the unresolved failure instead of creating endless continuations.
        return {"systemMessage": reason + " Cleanup remains incomplete; disclose this in the final response."}
    return {"decision": "block", "reason": reason}


def run(*args):
    completed = subprocess.run(
        list(args), cwd=ROOT, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, text=True, timeout=120,
    )
    if completed.returncode:
        raise RuntimeError(completed.stdout[-4000:])


def handle(event):
    session = event.get("session_id")
    if not session:
        return feedback("Rider cleanup hook received no session_id.", event.get("stop_hook_active"))
    key = hashlib.sha256(session.encode()).hexdigest()
    directory = Path(git("rev-parse", "--git-path", "codex-rider-cleanup").decode().strip())
    if not directory.is_absolute():
        directory = ROOT / directory
    state_path = directory / (key + ".json")
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    current = snapshot()
    if event.get("hook_event_name") == "SessionStart":
        if not state:
            save(state_path, {"baseline": current, "cleaned": {}})
        return {}
    if "baseline" not in state:
        return feedback(
            "Rider cleanup has no SessionStart baseline. Restart this session after trusting both hooks; "
            "manually clean any files already edited before the restart.", event.get("stop_hook_active"),
        )

    touched = {
        name: digest for name, digest in current.items()
        if digest != state["baseline"].get(name)
        and digest != state.get("cleaned", {}).get(name)
    }
    if not touched:
        return {}
    backend = [
        name for name in touched
        if name.startswith("backend/") and Path(name).suffix in BACKEND_EXTENSIONS
    ]
    unsupported = sorted(set(touched) - set(backend))
    if backend:
        masks = [name.removeprefix("backend/") for name in backend]
        if any(any(character in name for character in ";*?[]") for name in masks):
            return feedback("Cannot safely filter these cleanup paths: " + ", ".join(backend), event.get("stop_hook_active"))
        try:
            run("dotnet", "tool", "restore")
            run("dotnet", "build", "backend/SlotMathLab.slnx")
            for profile in ("Built-in: Reformat Code", "Built-in: Full Cleanup"):
                run(
                    "dotnet", "jb", "cleanupcode", "backend/SlotMathLab.slnx",
                    "--settings=.config/rider-cleanup.DotSettings",
                    "--profile=" + profile, "--include=" + ";".join(masks),
                    "--no-build", "--no-updates",
                )
        except (RuntimeError, OSError, subprocess.TimeoutExpired) as error:
            return feedback("Rider cleanup failed: " + str(error), event.get("stop_hook_active"))
        cleaned = snapshot()
        state.setdefault("cleaned", {}).update({name: cleaned[name] for name in backend if name in cleaned})
        save(state_path, state)
    if unsupported:
        return feedback(
            "JetBrains CLI cannot provide solution-based Full Cleanup for these files. "
            "Run Reformat Code and Full Cleanup in Rider and disclose any unavailable pass: "
            + ", ".join(unsupported), event.get("stop_hook_active"),
        )
    return {"decision": "block", "reason": "Rider Reformat Code and Full Cleanup completed. Review the cleanup diff and run relevant validation before finishing."}


def main():
    event = json.load(sys.stdin)
    try:
        result = handle(event)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        result = feedback("Rider cleanup hook failed: " + str(error), event.get("stop_hook_active"))
    print(json.dumps(result))


if __name__ == "__main__":
    main()
