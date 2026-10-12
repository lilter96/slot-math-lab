# Graph editing contract

Status: proposed implementation contract, based on `00_MAIN_AUDIT.md`.
Applies to BOTH requested studios. Repository invariants remain binding.

## 1. One executable source of truth

The committed executable model is the existing GraphConfig. The two studios and the existing Logic Graph edit the same model. No independent reels engine, payout evaluator, bonus runtime, or second persisted mathematical model is introduced.

```text
Current project / variant
        |
Canonical GraphConfig + non-executable view metadata
        |
Source index and validated authoring bindings
        |                              |
Reel & Paytable Studio           State Machine Designer
        |                              |
Staged typed edit / lowering to a candidate GraphConfig
        |
Dependency closure + source preconditions + model validation
        |
Atomic commit to existing project -> existing Graph editor
        |
Existing compiler, play/evaluate/run APIs and interpreters
```

View models are derived. A temporary editable form or machine IR is allowed, but it cannot become a second authority that silently overwrites graph changes. Executing a model must never require the two studios to be installed.

## 2. Binding representation

Implement typed authoring bindings outside Core/compiler/interpreter execution. The names below are proposed interfaces, not existing APIs.

A binding includes:

- A stable binding ID, model/project revision and authoring schema version.
- Structural source references: definition path, instance path where relevant, node ID, expression ID/path, table/state location, and row/outcome identity. Array offsets are resolved from stable IDs where possible.
- Read-set and write-set. The dependency closure includes generated derived values and executable consumers, not just the visible field.
- Exact value/unit metadata and board coordinate encoding.
- Capability state: editable, computed, custom/unresolved, or inapplicable, with a reason and a source link.
- Preconditions/fingerprint for all owned source fragments and dependencies.
- Definition-versus-instance scope and the known list of impacted uses.

A binding is not a heuristic like `if (gameId === 'nine-lives')`. Game names and symbols may be displayed, but numerical meaning must come from source data and verified contracts.

Use three entry paths:

1. New authored primitives/templates emit graph fragments and versioned non-executable binding descriptors together.
2. Existing bundled graphs are adapted using their actual structural contracts. All five families and variants in the acceptance matrix must be covered. Node labels may be hints, never proof.
3. Imported custom graphs receive generic source indexing and safe structural recognition. A custom expression/region that cannot be reinterpreted is retained as a Custom graph operation. The rest of the graph remains editable. Manual binding is allowed only with explicit paths, validation and a visible scope preview.

Recognition is an AUTHORING operation. Do not add named graph-pattern dispatch, inference or optimized behavior to the mathematical runtime. Shared adapters must pass renamed/duplicated graph tests and work on user-created graphs with the same contract.

## 3. Atomic edit transaction

Use one domain edit/undo boundary. Do not update Zustand piecemeal and try to repair consistency afterwards.

1. Snapshot the COMPLETE current graph, including unsaved nested edits, without navigating away. Reuse `peekRootProject` or the current worktree's safer equivalent.
2. Capture project identity and revision, selected variant and relevant binding fingerprints.
3. Parse the user's draft exactly. Incomplete input remains a draft; do not coerce empty to zero.
4. Produce an immutable candidate by changing the selected source plus its proven dependency closure. Retain every untouched field, edge ordering, expression, plugin reference and custom extension held by the project.
5. Show a semantic change summary: edited value, affected mechanics/uses, dependent regenerated fields, units, and any intentional effect on random sampling structure.
6. Validate supported ranges/types and graph structure locally; submit the complete candidate to the existing backend validation path. Where validity depends on runtime expressions, perform the targeted execution check or surface that execution has not yet been proven.
7. Commit only if the request still belongs to the same project/revision and all source preconditions still hold. A late success after cancel, project switch, or another edit must not commit.
8. One undo restores the entire change. Recompute projections and invalidate only evidence whose executable source no longer matches.

On failure, leave the original executable graph unchanged and keep the user's draft. Do not post an empty substitute model. Preserve the ability to download an editable draft even if it cannot execute.

## 4. Round-trip guarantees

These are release gates, not optional conveniences:

- **No edit:** opening/closing either studio and exporting does not change executable JSON, identifiers, array order, exact values, payout precision or random draw order. Formatting of exported JSON may differ; semantic content may not.
- **Edit then read:** reopening either view shows the committed graph values, not cached form defaults.
- **Graph edit then studio:** edits in the existing graph invalidate/rebuild corresponding bindings. An ambiguous change marks only the affected projection custom/unresolved; it never restores the old definition over the user's work.
- **Locality:** a verified patch modifies only its declared write-set/dependency closure. Test untouched opaque data and unrelated subgraphs.
- **Project isolation:** the same graph contents in two projects do not authorize a pending edit to cross project boundaries.
- **UI-only change:** pan, zoom, selection, ordering of UI tabs and chart layout do not modify executable identity or invalidate a run.
- **Graph-only portability:** raw exported GraphConfig runs through the current backend without the designer or any binding registry.

## 5. Derived fields are part of the edit

### Reel source

A physical strip binding owns the actual base strip, its wrap padding, stop domain/weights and verified derived conditional/refill uses. Never treat the wrap tail as extra random stops. Resizing or changing symbols may regenerate bounded helper fragments, but only when the current fragments match the binding's contract. Handle weighted/nonuniform stop distributions explicitly: preserve existing weights for retained stop identities; require a policy for added/deleted stops rather than silently resetting to uniform.

Woodland's bought-entry conditioning must be recomputed from the edited strip and trigger contract. Keeping old conditional pattern tables after changing a strip is a mathematical bug even if every component renders correctly.

### Payout source

Update the executable lookup and normalized declared paytable together. Exact payouts are in the displayed unit, which may be credits, total-bet multiples or round-price multiples. A changed ordinary stake is not the same as a changed bought-feature price.

A derived all-wild row, scatter extension, cluster stride/minimum/eligible-symbol set and baked pruning conditions must stay synchronized. Equal numbers do not establish that one row is derived from another. The binding must carry or validate that relationship.

Differentiate a game-rule payout cap from a computed maximum bound used to describe an uncapped model. Do not silently increase a game-rule cap or preserve a now-invalid computed bound. Display the dependency before applying.

### Shared and generated fragments

Prefer editing values in place. If topology must change, use a reusable graph authoring transformation with explicit ownership and a source diff. Do not rerun a whole bundled `createGameGraph()` over the current project. A factory may serve as an independent comparison/reference for a known conforming fragment, not a replacement for user data.

Changing one instance of a shared definition may require copy-on-write. Preserve its transitive expression/library dependencies and clarify whether state data remains shared. Node namespace changes alone do not isolate state.

## 6. Numbers and random identity

Inspect the current numerical boundary before implementation. Backend static Draw weights/values are `long`; JavaScript `Number` cannot exactly represent every such value. Preserve exact tokens with a lossless representation/transport appropriate to the existing serializer, or explicitly block unsupported edits without altering the original value. Never silently round. Expression integer/rational constants and rational paytable strings must retain their type and exact value. Do not send a bigint through plain JSON.stringify without a deliberate encoding contract.

A symbol ID may be a numeric-looking string and must remain a symbol ID. A Draw's numeric `value` is not necessarily its payout. Static weights, dynamic-weight expressions and their preview states must remain distinct.

No-op edits preserve sampling identity. Changed weights naturally change the distribution. Structural edits may change how many random values are consumed; flag that fact rather than promising same-seed equality across intentionally different programs. For unrelated refactoring or simple payout edits, preserve existing draw order, including apparently unused prize draws that the graph deliberately performs.

## 7. Metadata and persistence

GraphConfig in the reviewed main has no authoring-specific metadata property. Choose a versioned project envelope or supported project-side metadata storage. Prove that the actual storage/export/import path retains it. Do not put unknown fields into GraphConfig and assume the C# serializer retains them.

The envelope contains one executable graph and non-executable view data: layout, stable source mappings, binding versions, desired display names, and reconstruction hints. Do not keep a competing copy of reel weights/payouts/transitions there. Send only the graph to current calculation APIs. A raw graph import without metadata must still preserve and execute its entire source.

Reuse current project persistence if the worktree already has it. This task authorizes the minimal persistence integration for the two views, not a new account system, project-dashboard redesign or shared-workspace product.

Autosave must expose pending/saved/error state. Verify both reload AFTER confirmed save and immediate user reload during a pending edit/save. Do not fix the latter test merely by waiting for a debounce: choose and implement a real recoverability contract.

## 8. Suggested code boundaries

Adapt names to existing worktree code; avoid duplicate utilities:

```text
frontend/src/lib/authoring/
  sourceIndex / bindings / exactValues
  graphEdits / dependencyClosure / transactions
  reelBindings / payBindings / lineBindings / weightBindings
  stateMachineProjection / stateMachineLowering
  viewMetadata / reconciliation
frontend/src/components/authoring/
  common controls, source inspector and change summary
  reel-paytable/*
  state-machine/*
frontend/src/pages/
  ReelPaytableStudio
  StateMachineDesigner
```

Pure transformation tests belong at the frontend logic layer. Generic emitted graphs and runtime semantics are exercised through existing backend tests/APIs. No new game-specific behavior belongs in Core or API. Small, explicit presentation/persistence API changes are permitted only where the feature actually requires them; they must not become a hidden runtime.
