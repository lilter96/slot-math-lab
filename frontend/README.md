# Slot Math Lab frontend

React 19, TypeScript and Vite power a mathematical workbench where each user can own many private game/model projects. React Router handles navigation, Zustand holds editing state and React Flow renders the model graph.

## Projects and ownership

The application starts at **My projects** (`/projects`). Users can create blank projects, import personal JSON or make independent template copies. Switching projects preserves their edits and nested context. Each selected model supplies its own geometry, symbols, mechanics and supported editors; Nine Lives is one reference example.

Browser workspaces are scoped to verified user identity. Server configurations, runs and experiments are owner-only. Sharing and collaboration are deferred. Local drafts autosave within browser capacity; saving a server version is explicit. Automatic cross-device draft synchronization is not implemented.

See the [product README](../README.md), [private workspace contract](../docs/design/PRIVATE_WORKSPACES.md) and [UX plan](../docs/design/UX_MASTERPLAN.md) for behavior, persistence and limits.

## Local development

Use Node.js 22. From this directory:

```sh
npm ci --legacy-peer-deps
npm run dev
```

Backend startup and local dependencies are described in the [product README](../README.md). `npm run build` checks TypeScript and produces the Vite bundle; `npm run lint` runs ESLint. Generate schema/API types using `npm run generate-types` and `npm run generate-api` rather than editing generated files manually.

## Verification

Follow [AGENTS.md](../AGENTS.md): ordinary iterations run only affected or new tests. Full suites run after a substantial integrated feature or an explicit user request; documentation-only edits need no tests.

Unit tests live in `unit/`, browser workflows in `e2e/`. Select relevant files with Playwright, for example:

```sh
npx playwright test -c playwright.unit.config.ts unit/<affected-file>.spec.ts
npx playwright test e2e/<affected-file>.spec.ts
```

The [testing guide](../docs/TESTING.md) explains browser prerequisites and release checks. Available suite commands are not a checklist to execute on every iteration.
