# Private-workspace migration and rollback plan

This is a plan for a future authorized deployment, not evidence of a production migration. The current local redesign has not been pushed or deployed. See [PRIVATE_WORKSPACES.md](PRIVATE_WORKSPACES.md) for account configuration and actual persistence behavior.

## Compatibility and ownership

Existing graph JSON, authored IDs, AST, nested definitions and pinned mathematical inputs remain canonical. New project/storage identities separate independent copies of the same model. Browser draft schema remains `slotmath-draft-v1`, with added project collection/identity fields. A legacy draft becomes one personal project on load. Signed-in keys are suffixed by the verified user identity; anonymous keys stay separate. Existing anonymous work is transferred explicitly by JSON export/import, never silently assigned to an account.

Server configurations, runs and experiment manifests now contain `OwnerId`. Legacy ownerless records map to `user:<Auth:User>` when the legacy operator is configured. Local mode without that account maps them to `anonymous`; production without a legacy operator maps them to `legacy-unassigned`. Adding `Auth:Users` must not give the first new account ownership of all old records. Preserve the original legacy `Auth:User` value while introducing additional accounts.

The archive is a single-process encrypted snapshot store. `Storage:Directory` defaults to `/data` in production and contains `configs.bin`, `runs.bin` and `experiments.bin`. `Storage:Key` is the Base64 representation of the existing 32-byte key. Keep key material in the deployment secret store; do not print or commit it. New snapshot writes persist owner metadata. Account IDs are ownership identities: changing an ID without a separate ownership migration makes its old data inaccessible.

## Staged upgrade

1. Record the currently deployed revision, build artifacts, feature flags and account identifiers. Export important browser models separately; saving a server version is explicit and local autosave is not cross-device backup.
2. Drain or cancel admitted work, stop writes and stop the single API process before taking a coherent archive backup. Back up the whole storage directory, its matching encryption-key version, authentication configuration and related database state under the deployment's existing secure backup process. Do not copy live snapshots independently while they change.
3. Restore the backup into an isolated staging environment with separate writable storage and service ports. Preserve the legacy operator mapping; add unique provisioned accounts without changing existing IDs. Start the candidate API and matching frontend together. Feature flags are read at API startup, so changing them requires a restart.
4. Verify the migrated legacy operator's archive, a fresh account's empty archive, multiple projects per account, independent same-model versions, pinned run recovery and foreign-ID denial including realtime subscriptions. Verify JSON round trips and legacy inbound import routes. Run only migration/ownership/compatibility checks for iteration fixes; required release gates apply to the final candidate.
5. Promote only after required cleanup, release verification and acceptance pass. Retain the previous artifacts and coherent pre-upgrade backup for the rollback window. Monitor authentication failures, ownership denials, snapshot errors and job completion; do not enable sharing or new features merely because the redesign is deployed.

## Rollback constraints and procedure

An older API that lacks owner filtering must not serve a multi-user archive written by the new API. Ignoring an unknown `OwnerId` JSON field would not preserve isolation. Likewise, an older frontend can overwrite a multi-project browser draft with its single-draft format. A simple binary downgrade against current data is not a safe rollback.

1. Freeze writes, stop the candidate API and preserve the current encrypted archive as a separate recovery copy. Have users export unsaved/new browser models before loading an older frontend where possible.
2. Prefer a forward fix or an ownership-aware previous release. For a downgrade to pre-ownership code, restore the matching pre-upgrade archive, keys/configuration and related database state as a coherent set; retain new data separately for a later reviewed import. Restrict access to the original operator or keep the service offline until isolation is verified.
3. Serve matching API/frontend artifacts. Do not silently reuse or overwrite a new multi-project browser namespace in an older client. Use an isolated browser profile for downgrade review and explicitly restore/import backed-up models.
4. Verify the original operator's data and supported routes before reopening access. Document the recovery point and any changes created after it; do not claim lossless downgrade or merge independent archives automatically.

## Recorded evidence and limits

`PrivateWorkspaceTests` covers encrypted restart and explicit legacy ownership, multiple configured production accounts and private API/SignalR boundaries. Browser tests cover project/account isolation, storage failures and explicit server save/open behavior. Those tests do not constitute a hosted migration or rollback rehearsal. Multi-instance persistence, load certification, registration/OIDC, public sharing and collaborative editing remain separate work.
