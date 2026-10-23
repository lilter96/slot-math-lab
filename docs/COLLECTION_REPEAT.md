# Repeat an editable subgraph over a collection

The Build page's **Repeat over collection** action wraps an existing Library
call with ordinary editable JSON atoms. It stores one body and collection data
rather than materializing one body per item. No ForEach kernel node, named
mechanic evaluator or runtime parameter dispatch is introduced.

## Authoring contract

Choose a call with one State input and output in the current graph scope. Set
the source array's state key, item type, maximum length, current-item key and
current-index key. Types include Number, String, Boolean, Array and Record.
The body must already read the selected binding keys. Its original literal
parameters and shared definition are retained. Wrapping changes how often this
call executes; it is an authoring operation, not a proof that a different
number of executions preserves a game's economics.

The designer previews the candidate, can inspect its complete JSON, then applies
it through whole-model compiler validation. Applying updates the current shared
definition, so its other uses also see the change. One undo restores the full
source and open subgraph context. Existing owner/project/source guards reject
stale previews, late asynchronous validation and undo across another project
or owner. The preview does not commit changes. Closing a pending validation
aborts the edit and restores keyboard focus.

The source, item and index keys must be distinct, nonempty and at most 128
characters; loop service keys are reserved. Bounds are literal integers in
0..4096. The item/index types are declared in the ordinary root stateSchema,
retaining compatible existing declarations and annotations. Conflicting
existing declarations are rejected rather than silently replaced. No initial
item value is invented.

## Editable atomic lowering

The new reusable definition has six authored nodes:

1. A bounded identity Map in ModifyState snapshots the source array once.
2. Loop checks its current iteration against snapshot length before each body.
3. A GetState exit node provides the original State continuation.
4. ModifyState writes the current index.
5. ModifyState writes the indexed current item.
6. The original Library call executes the body with its original parameters.

The external call retains its ID, ports and edges. Only its referenced definition,
label and parameters change; its former parameters now belong to the nested
body call. Unknown external source properties and original definitions are
preserved. The generated snapshot key must not collide with authored data.

The Loop cap is `maxItems + 1`: the ordinary cap-before-condition rule can check
normal completion after processing exactly maxItems entries. An overlong source
fails the bounded Map before any body execution; it is not silently truncated.
An empty source executes no body and leaves existing item/index values alone.
The original item order and body Draw order remain. Changing the source array
inside the body does not change the already captured sequence.

Bindings are ordinary state fields and retain their final values after a
nonempty repeat. They are not lexically restored. Nested repeats need distinct
item/index keys; a closure check rejects body writes to those selected bindings.
Explicit body writes to loop service keys also decline. Missing/recursive
subgraphs, nesting beyond 50 levels and unknown write effects decline the
transform.
Additional body nodes remain freely editable JSON after applying; compiler and
runtime validation continue to apply to subsequent edits.

## Generic compiler support

Named expression references now resolve before validation/schema inference,
inside the compiler's existing coded-error boundary. This allows parameterized
recurrences to infer actual operand types, while missing/cyclic references
remain compile errors. It also checks named String and Boolean values using
their actual types rather than a provisional numeric reference container.

Initial state shapes seed recursive inference when an intermediate field is
still unknown during an earlier pass. This seed is used only for internal
inference; returned field proofs still reflect actual writers. Delayed nested
array/record bindings can therefore refine a numeric recurrence without
permanently poisoning its self-reference. Incompatible writers and invalid
structured operands retain error/unknown proofs. No game or mechanic name
participates in either compiler correction.

## Verification and limits

Verification covers 579 unique selected Core cases, including 11 new compiler
shape/reference controls. The initial run passed 577 and exposed a global
registry race in two existing tests. An unrelated primitive fixture cleared a
registry it never used; removing that cleanup and rerunning both affected
classes passed all 41 cases. The other successful cases were retained.
Five API previews and one cache check passed; the cache check used the task's
isolated PostgreSQL after its initial default database connection was refused.
No production/shared database was used.

Frontend verification passed 18 new authoring cases plus nine existing
transaction cases, scoped ESLint, TypeScript/Vite build and six selected
API/browser scenarios against the built frontend. Both mandatory Rider profiles
completed all five touched C# files and all six frontend source/test files;
final scoped formatting passed. Full suites were not run. Independent controls exercise snapshot mutation, empty
and exact bounds, overlong input, nested bindings, Array/Record payloads,
ordered stochastic bodies versus an explicit unrolling and the following
categorical choice. UI controls cover preview, JSON export, apply/undo,
canceling late validation, keyboard focus and desktop/phone layout including
expanded JSON and the dialog footer.

This authoring form reduces repeated body representation. It does not promise
a spin-rate improvement: runtime loops can lose to compiler-shared unrolled
code on small domains. R17 still requires migrations and complete-round
measurements for all current games. No production-game multiplier is claimed.
