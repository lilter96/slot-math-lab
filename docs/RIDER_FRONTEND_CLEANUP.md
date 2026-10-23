# Scoped frontend cleanup in Rider

Rider 2026.1.2's `bin/format.sh <frontend files>` fails in this workspace with `solution can't be null` before formatting begins. Opening a real solution in Rider and running the two built-in cleanup profiles succeeds. Do not treat a formatter invocation or a passing lint/build check as completed cleanup.

For frontend files outside the backend solution, use a temporary solution containing only the exact task files as solution items. One existing C# project supplies solution context; exclude that project from the selected cleanup scope. This does not change the application solution or require backend tests.

1. Write one repository-relative source path per line to a temporary manifest. Include new files and edits committed earlier during the task. Review this list; do not select the entire frontend, repository or another task's files.
2. Generate the temporary solution below from the repository root. Set `TASK_CLEANUP_MANIFEST` to the reviewed manifest.
3. Open the printed `.slnx` path in Rider. In Solution Explorer, select **TaskFiles**. Choose **Code → Reformat and Cleanup…**, confirm **Directory 'TaskFiles'**, select **Built-in: Reformat Code**, and run it.
When making further edits outside Rider, close the isolated IDE first, apply
those edits, then reopen the temporary solution before both profiles. Cached
versions of external solution items can overwrite newer disk edits when cleanup
or automatic save begins; Reload All from Disk did not reliably prevent this
in the 2026-10-22 session. Never edit while cleanup is running. Verify the
intended changes survived both profiles before running regressions.

4. Repeat on the same folder using **Built-in: Full Cleanup**. Save all files. Check Rider's `idea.log` for both successful completions and the exact selected frontend files; keep the scope/profile evidence.
5. Review the diff, check for changes beyond formatting/import cleanup, run scoped lint and relevant checks. Repeat both passes after any subsequent source edit. Documentation-only edits require no tests.

```sh
export TASK_CLEANUP_MANIFEST=/tmp/my-task-cleanup-files.txt
python3 - <<'PY'
import os
import tempfile
from pathlib import Path
import xml.etree.ElementTree as ET

repo = Path.cwd().resolve()
manifest = Path(os.environ['TASK_CLEANUP_MANIFEST'])
files = list(dict.fromkeys(manifest.read_text().splitlines()))
if not files:
    raise SystemExit('The cleanup manifest is empty')

solution = ET.Element('Solution')
folder = ET.SubElement(solution, 'Folder', Name='/TaskFiles/')
for name in files:
    path = (repo / name).resolve()
    if not path.is_relative_to(repo) or not path.is_file():
        raise SystemExit(f'Invalid task file: {name}')
    ET.SubElement(folder, 'File', Path=str(path))
ET.SubElement(solution, 'Project',
              Path=str(repo / 'backend/SlotMath.Core/SlotMath.Core.csproj'))
ET.indent(solution)
destination = Path(tempfile.mkdtemp(prefix='slotmath-rider-cleanup-')) / 'TaskCleanup.slnx'
ET.ElementTree(solution).write(destination, encoding='unicode')
print(destination)
PY
```

Use the owner's Rider code-style settings and each source file's applicable EditorConfig. Do not replace Full Cleanup with a custom profile or commit the temporary solution. An isolated Rider instance may use separate config/system/log directories and a virtual display; preserve the owner's settings and leave their IDE session alone.

Verified on 2026-10-12 for all **92** graph-studios source/test files: Reformat succeeded at 23:33:26 and Full Cleanup at 23:33:58. The latter ran Optimize Imports and Reformat through Rider's frontend engine. See the maintained [delivery summary](design/graph-studios/DELIVERY.md); generated cleanup manifests and Rider log excerpts remain local in the ignored evidence directory. Both profiles and their scope were captured locally; the previous failed CLI logs remain retained.

JetBrains documents [selected-file/folder cleanup and the built-in profiles](https://www.jetbrains.com/help/rider/Code_Cleanup__Index.html).

The same temporary-solution workflow applies to backend JSON fixtures when
`dotnet jb cleanupcode` reports them as **Skipping**. A CLI exit code of zero
does not certify those files. Select only the JSON files in TaskFiles and run
both built-in profiles in an isolated Rider instance. Verified with Rider
2026.1.2 on 2026-10-24: two Reachable JSON subgraphs completed both profiles
with `frontend result = Success`; the log named both JsonFile inputs and no
backend cleanup files. Keep the profile/scope evidence outside Git.
