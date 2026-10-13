# Product and design documentation

The product supports **many users, each with many private projects**. A project contains a game/model and its editing context; users can also have their own personal materials. Nine Lives is one example, not the application's only game. Start at **My projects**. Sharing, collaboration, invitations and public project links are deferred.

Use these documents together:

- [Product requirements](../PRD.md): ownership, separate project identities and mathematical contracts.
- [Architecture](../architecture.md): user/project/model boundaries and the shared generic math engine.
- [Authoring guide](../authoring-guide.md): starting with a private project and editing the selected model.
- [Operations](../PRODUCTION.md): multiple configured accounts, archive ownership and deployment limits.
- [UX masterplan](UX_MASTERPLAN.md): project library, navigation and selected-model editing.
- [Private workspaces](PRIVATE_WORKSPACES.md): data ownership by user/project/source, implemented persistence, account isolation and current limits.
- [Design system](DESIGN_SYSTEM.md): interface requirements for the project library and workbench.
- [Implementation handoff](CODEX_HANDOFF.md): execution requirements.
- [Implementation status](IMPLEMENTATION_STATUS.md) and [requirements audit](REQUIREMENTS_AUDIT.md): delivery evidence and outstanding work.

Generated screenshots, logs, audit JSON and per-iteration review notes belong in ignored local QA/evidence directories or CI artifacts. Keep only maintained product/design documents, acceptance summaries and reproducible input fixtures in Git. Historical captures do not narrow the current product scope. Workspace-level brief copies live in `../../../docs/design`; update the maintained documents here first.

Documentation-only changes require no tests. Other local changes follow the affected/new-test policy in [AGENTS.md](../../AGENTS.md).
