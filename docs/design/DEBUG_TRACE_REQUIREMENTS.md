# Trace capability boundary and follow-up contract

Delivered /debug uses /api/play/round with trace=true. Its receipt contains the exact authored config, returned config hash, seed, round index, rational payout and full returned state. Supported Nine Lives histories contain before/after boards, cascade/spin indices, multipliers, hit totals and row-major hit positions. They are read from the authored snapshot fields, never reconstructed by a game evaluator in the UI.

The response does not provide engine/build version, trace event IDs, source-node paths, timestamps or RNG stream mapping. The UI explicitly reports missing node mapping and limits replay evidence to a full observed response comparison on the current server. There is no claim of compatibility across engine versions, universal event attribution or a debugger breakpoint.

A future generic instrumentation contract would need:

- A receipt identity: canonical configuration hash, engine build/version, stream scheme, all execution inputs and round identity.
- Compiler provenance: an immutable mapping from each compiled instruction to its authored node ID and nested mechanic path, including repeated references to the same definition.
- Ordered generic events: event ID, receipt identity, instruction/source path, atomic operation, typed state delta and optional observed payout delta. Optional timing must not be confused with mathematical ordering.
- RNG observations: existing stream identity and draw ordinal recorded without consuming additional draws or changing scheduling. No synthetic node-level RNG attribution inferred from histories.
- Explicit coverage and truncation: missing events, size/budget ceilings and failed/incomplete rounds must remain distinguishable from complete traces.

Verification before enabling graph links: trace on/off must give identical payout and mathematical state for pinned seeds; every source reference must resolve to the pinned config; nested/repeated references must remain distinct; replay must validate build and stream identity; bounded event recording must never change execution or RNG order. These are generic compiler/interpreter concerns. No game-specific kernel dispatch or new atom is proposed by this redesign.

Per-feature RTP anatomy is similarly unavailable without a declared collection/overlap/unit convention. Existing saved-run component-accounting and scoped measurement surfaces remain authoritative. A diagram dividing total RTP into arbitrary feature percentages would not meet this contract.
