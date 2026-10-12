# Coupled conditional reel editing

Source c4d4c9e. Frontend source adaptation only; no kernel/compiler/interpreter/PRNG change, new atom or game-specific execution dispatch.

## Behavior and authoring contract

A complete existing spinReelsShowing/showingState shape is matched against its authored state, typed expressions, nodes, ports, edges and integer weights. Symbol/threshold are recovered only when that complete match is unambiguous. Edits rebuild circular periods/tails, ordinary shared stops, showing-pattern counts/weights and every conditional stop subset together. Only the affected reusable definition is rebuilt; other game data/graphs stay intact.

The original materialized graph may not distinguish different thresholds after a strip becomes all one symbol. To retain the recovered authored rule through canonical export/import/server serialization, the definition now includes existing typed constant expressions __reelEditorSymbol (String) and __reelEditorAtLeast (Integer). These use the existing AST contract; no extra initial-state keys, hidden alternate model, evaluator or RNG operation is added. They are accepted only if the whole generated conditional graph and pattern table match them. User-authored/malformed/inconsistent declarations remain read-only. The constraints describe source authoring; actual execution still consumes the compiled graph and its tables.

Surviving node IDs/labels/unknown metadata and definition metadata are preserved. If changing group topology would delete a node with authored extra data or a custom label, the transaction refuses rather than dropping it silently. Modified/unknown stop rules, extra external pattern consumers, multiple independent shared-pattern consumers, local state overrides or plugins are unsupported until their source mapping is proven. Empty/unknown strips, impossible events, more than 4,096 pattern combinations or weights outside JavaScript's safe exact integer range fail before canonical mutation. The API's DrawWeight is long; no false Int32 engine limit is imposed (10^10 is tested).

The UI distinguishes ordinary raw-strip frequency from conditioned stop probabilities. It shows the retained minimum/symbol/source rule and recovery by the Apply action; failed inputs remain an unapplied draft, with Reset available. This contract does not claim a universal arbitrary graph-to-form conversion.

## Checks and real artifacts

- Initial affected/new units 8/8 (five ordinary + three new conditional); changed guard/integer cases received only the three conditional checks afterward, 3/3. Final recovery wording received only its affected rejection case, 1/1.
- Chromium affected reel workflows 7/7. Strengthened all-one-symbol degeneration/export/import test alone reran, 1/1.
- New/changed conditional workflows Firefox/WebKit 4/4. Actual bought-game Apply/compiler/Undo/reimport and independent small conditional oracle in both engines.
- Independent Cartesian arithmetic: 01/10/11 qualify, first=1 in 2/3; after appending another zero on the first strip, four stop combinations qualify and two have first=1, giving 1/2. Actual evaluate/light returns those values. Raw first-strip frequency is 1/3, which is deliberately not shown as the conditional probability. Undo restores full authored JSON and the full observed seed-7 response on the current server.
- The all-One degeneration returns actual expectation 1 and remains editable after JSON import with its original threshold 1. No ambiguous rule is guessed.
- Build/scoped ESLint pass. Late copy/error placement changes received build/lint, an affected rejection check and actual rendered recovery captures; no broad suite repeat.
- Twelve final captures: actual bought Woodland before edit, applied edit and impossible-event failure, at 1440×900, 1280×800, 1024×768, 390×844. Zero document overflow and zero editor proof badges; metadata conditioning-audit.json. Desktop applied and phone failure inspected; recovery message moved above the table after initial review.

Logs: conditioning-unit.log, conditioning-final-unit.log, conditioning-recovery-unit.log, conditioning-browser.log, conditioning-degenerate-browser.log, conditioning-cross.log, conditioning-*-build.log. Images conditioning-{before,applied,impossible}-{1440,1280,1024,390}.png stay local under the repository QA convention. No complete suites/backend tests ran; actual compiler/evaluation/round API endpoints were exercised.

Required Rider Reformat still exits 3 (frontend outside the solution); Full Cleanup remains unavailable. Actual per-edit attempts are reel-conditioning-rider.log and conditioning-*-rider.log. No required cleanup success or release readiness claimed.

## Remaining plan work

The previously missing bought-feature coupling now has implementation and scoped evidence. Continue other table/graph keyboard/drag alternatives, async project/context guards, scored design review/practitioner feedback and Core/Rider release checks. Goal completion remains unproven until the complete requirement audit is satisfied.
