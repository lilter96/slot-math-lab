# Personal projects and ownership

The user clarified on 2026-10-12: one user may own many games, models and personal materials. Projects are private; sharing/collaboration is out of scope. This overrides the earlier design brief's assumption that Nine Lives is the primary entry experience.

## Product ownership contract

- A user owns a collection of independent projects, rather than one global game. Selecting a project selects the model and editing context used by its workbench.
- Project-specific data, drafts and results must retain their project/source association. A result from one game must not become evidence for another when switching projects.
- Personal materials belong to their user. Project-specific materials must also retain their project association; mentioning personal materials does not claim that a general-purpose file library has been implemented.
- User identity, local project identity, authored model ID and server configuration/version identity serve different purposes. Matching authored IDs or copying the same template must not merge ownership, drafts or version histories.
- Future sharing would require a separate access model. It is not part of the current feature set, and there are no current cross-user access grants.

## Data scope for multiple projects

| Data | Ownership and association |
|---|---|
| Project collection and personal materials | Belong to the verified user. Materials may have a project association; a general-purpose material library is not yet implemented. |
| Game/model JSON and editor draft | Belong to an independent private project. Editing project A must not change project B, including when both came from the same template. |
| Layout and open nested mechanic | Retained with the project's editing context when switching away and back. |
| Saved configurations and versions | Owner-scoped server records with storage IDs separate from authored model IDs. Saving a draft to the server is explicit. |
| Runs, experiments and mathematical evidence | Owner-scoped and tied to the evaluated configuration/source. Selecting another project does not make old evidence current. |
| Templates and reference games | Starting examples copied into private projects. They grant no access to another user's authored projects. |

For example, one user can keep projects **Game A**, **Game B** and **Game A — experiment**. The experiment is an independent copy: changing its weights leaves Game A's draft intact. Another user has a separate collection and cannot open these projects by obtaining their IDs. Future sharing requires an explicit access design; no sharing mechanism is part of the current scope.

## Implemented behavior

`/` opens `/projects`. A fresh workspace does not automatically create Nine Lives. The personal library creates independent copies of five migrated games, the separate Nine Lives reference model, two starter models or a blank graph. It supports search, opening, duplication, renaming and personal JSON import. Opening another project retains the current editor draft, positions, open nested mechanic and recovery state. Each project has a generated identity independent of the authored graph ID. `?project=nine-lives` remains an explicit reference-model entry point; legacy inbound `?load=` links remain import compatibility. Export no longer creates share links.

The same data/compiler/AST model powers every project. Overview reads geometry, symbols and definitions from the selected graph. A coordinate diagram is never a generated spin. Data and feature forms retain guarded source mappings and explicit unsupported states. Debug executes any current graph and returns raw state when no supported board-history presentation exists. `/play` is labelled **Reference demo**, rather than claiming to preview every game. The existing `/games.html` catalog remains available.

Browser draft, project collection, simulation session/history, measurement workspace and demo-session keys are namespaced by the user ID returned by a validated `/api/auth/status` response. Both application entry points verify identity before importing persisted stores. Explicit HTTP 429 status checks honor Retry-After for at most three attempts while stores stay unopened. Other failed status reads display a retry screen; required unsigned sessions display sign-in before mounting the workspace. Account changes reload the application, so cached forms, query results and receipts cannot cross identities. Anonymous development retains the old storage keys for compatibility. Existing anonymous drafts are not silently assigned to a signed-in account; export/import is the explicit transfer path. Browser namespaces protect application behavior, not against someone with access to the same OS account or browser developer tools.

Saved server versions can be listed and opened from Projects. Browser drafts autosave locally, within the browser storage quota. A failed write displays an explicit backup action while the in-memory edits stay available; saving to the server remains an explicit Export action. This is not automatic cross-device synchronization. The server owns canonical configurations and pinned calculations; the browser owns unsaved editing layout.

## Server boundary

Config entries, runs and experiment manifests record an `OwnerId` derived from validated JWT claims, never from request bodies. HTTP store reads/lists/version access/deletion/update/admission/cancellation/diagnostic retention are filtered to the current owner. Foreign identities receive the same missing-record response as unknown identities. SignalR subscription/snapshot methods explicitly compare the hub connection principal before joining the run group. Hosted workers have no HTTP context and can process pinned records across owners; runs inherit the pinned config's owner. Production requires authentication for API/hub access.

Creating a configuration always allocates a fresh storage ID. Its authored `GraphConfig.Id` remains intact when present. Two users, or two projects of one user, can create copies of the same template without appending to one shared version history. Updating a stored config preserves its owner. Math compilation, PRNG, sampling/exact semantics and graph atoms are unchanged.

Encrypted legacy records without an owner belong to the configured legacy `Auth:User`. In development without that setting they belong to the anonymous workspace. Production with only new account configuration leaves old ownerless records `legacy-unassigned`; it does not assign them to the first user who logs in. Preserve legacy account configuration when upgrading an existing deployment. Take a backup of encrypted storage and its key before any production migration. No production migration was performed here.

## Account configuration and limits

Production accepts configured PBKDF2 accounts in `Auth:Users`, preserving the existing `Auth:User` / `Auth:PasswordHash` pair:

```json
{
  "Auth": {
    "Users": [
      { "UserId": "alice", "PasswordHash": "<provisioned PBKDF2 hash>" },
      { "UserId": "bob", "PasswordHash": "<provisioned PBKDF2 hash>" }
    ]
  }
}
```

Environment equivalents are `Auth__Users__0__UserId` and `Auth__Users__0__PasswordHash`, etc. Secrets belong in deployment configuration, not source control. Existing hash verification requires PBKDF2-SHA256, 210000–1000000 iterations, at least 16 salt bytes and a 32-byte derived key. Production still requires unique JWT and encrypted-storage keys. Development/testing token issuance remains development-only and cannot establish hosted production account security.

No public registration, password-reset flow, OIDC provider, teams, roles, invitation, publication, public share lookup or collaboration was added. The existing store is a single-process encrypted snapshot archive with bounded compute concurrency; owner isolation is not evidence of large-scale throughput or multi-instance storage safety. A scalable deployment requires its own persistence/capacity work and validation. The local-only experimental `/api/persisted/cache` interface remains outside production routing and is not the private project archive.

Inspector pinning is a presentation preference scoped to the verified user, independent of model JSON. It can remain open without selection; switching projects clears the previous node. Close/Escape clear the pin, while reload restores an explicit pin.

Overview reads the latest completed owned run matching the current model’s canonical server fingerprint. The original saved config/hash and result integrity are checked before showing numbers. The existing comparison convention excludes document ID but includes every other serialized model field; an identical owned copy may reference an immutable matching run, clearly labelled with its original ID/version. It does not copy mutable simulation state into that project. Changed inputs, another user, late responses and unavailable reads cannot inherit its displayed proof. This read creates no calculation or server version.

## Migration and review

The main [UX plan](UX_MASTERPLAN.md), [design system](DESIGN_SYSTEM.md), [design reference](../../design-system/MASTER.md) and [Codex handoff](CODEX_HANDOFF.md) now use this multi-project scope. The [migration/rollback plan](MIGRATION_ROLLBACK.md) describes a future coordinated upgrade, legacy ownership mapping and downgrade constraints; no hosted migration has been performed.

## Verification

PrivateWorkspaceTests exercise two real JWT identities, independent copies, config CRUD/version denial, run launch/read/evidence/cancel denial, witness/verification denial, real SignalR subscription/snapshot rejection, owner worker completion, experiment child isolation, encrypted restart, explicit legacy ownership and multiple production credential accounts. Browser tests cover nested edits across different games, same-template independence, staged form isolation, verified-account changes, unavailable/unsigned session boot and validation finishing after a project switch.

## Browser model-number limits

Personal JSON imports (Projects, toolbar, Export and legacy inbound links) and saved-config response guards reject original number tokens outside ±9007199254740991, as well as nonfinite overflow. This prevents silent Int64 rounding during those reads; string metadata is preserved. Project-opening failures leave the current model available and display a specific error. Keep unsupported models as original JSON; full-Int64 browser authoring/read-only viewing is not implemented. Ordinary fractional values retain existing IEEE-754 semantics. Persisted workspace hydration now validates original tokens; rejected raw workspaces get exact owner-scoped recovery copies and a download action. Failed backups block active-draft writes, preserving original storage. Outgoing workspace snapshots are checked before autosave. Raw scalar/numeric-array state inputs now retain invalid/incomplete text without mutating the model; measurement AST JSON validates original tokens and preserves string constants. Legacy/native Inspector numbers now validate original text, integer domains, loop limits and weight totals before mutation; static table summaries use exact BigInt fractions. Draw canvas summaries now use exact static fractions and explicit declared dynamic modes. Malformed legacy DSL now retains editable state with an explicit serialization error and raw-draft download, while executable projections/exports and schema requests remain blocked. Strict outgoing configs validate numeric budgets. AI/explicit outgoing payloads, raw-draft restoration, remaining malformed paths and measurement/option boundaries remain under audit. See [serialization recovery](qa/SERIALIZATION_ERROR_REVIEW.md). See [Draw card evidence and current parser gap](qa/DRAW_CARD_REVIEW.md). See [Inspector numeric contracts](qa/INSPECTOR_NUMBER_REVIEW.md). See [raw numeric input review](qa/RAW_NUMBER_REVIEW.md). See [draft recovery](qa/DRAFT_RECOVERY_REVIEW.md). See [current precision evidence](qa/MODEL_JSON_PRECISION_REVIEW.md).
