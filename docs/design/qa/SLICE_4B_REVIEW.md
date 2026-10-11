# Source-backed payout editor

Model data now offers Weighted draws and Cluster payouts. The payout form is gated by all five symbol entries, sizes 5–30, matching executable coin-array values and metadata, the two unchanged cluster lookup ASTs, and the root conversion from coins into model units. It edits whole coin amounts and updates their metadata divided by 20. It preserves IDs, unknown fields, expression ASTs, caps and weights. Unsupported mappings show a graph link.

Build/lint pass, 125 units pass, 6 workbench browser tests pass. Inverse edits match the entire original Nine Lives fixture. Real API checks with seed 1 prove identical first boards and a five-cell symbol-1 cluster whose executable payout changes from 1 to 2 coins, with mirrored metadata 0.05 to 0.1. Input rejection, Undo and navigation persistence pass. Debug test now uses this known nonempty seed and requires actual hit highlights rather than conditionally skipping that assertion.

All requested screenshots captured as slice4b-payouts-*.png. Physical reel-strip UI remains unsupported for this weighted-cell model. CSV staging is a later local hardening task. Frontend Rider cleanup remains unavailable (BASELINE.md).
