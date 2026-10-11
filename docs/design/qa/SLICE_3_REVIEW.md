# Graph workspace review

Build and lint pass; 117 units pass. Feature/metric checks pass. Final graph/library/Overview interaction run: 9/9 pass against real isolated API, including nested extraction, reuse, empty definition, restoration and unchanged expected payout 22.

Screenshots: slice3-graph-1440.png, slice3-graph-1280.png, slice3-graph-1024.png, slice3-graph-390.png; slice3-inspector-1440.png. At 1440, graph canvas is 1008px wide with inspector closed; primary workspace excluding global navigation is 1240px (81.3%). This is a DOM/layout measurement, not a frame-rate claim. Screenshot review confirms clear hierarchy and reserved toolbar space, source tree replaces chips, selection opens contextual inspector. Medium viewports use an explicit drawer. Small screens permit selection/pan and disable node drag/connect/delete, with a visible explanation.

Changes also fix drop positions under zoom/pan using React Flow's screenToFlowPosition, use unique node IDs, add keyboard alternatives to dragging palette entries, remember each mechanic's viewport, preserve a recoverable draft for toolbar examples/imports, and persist that backup across reload. Closing inspector preserves selected node and focuses it.

Test adaptations use real search/disclosure controls. Old source assertions were retained. Two initial backup tests now initialize from a real coin share-link to verify restoration of that actual original model rather than of the auto-created Nine Lives draft. Local API stopped gracefully during an earlier pass (HTTP 502); service restart and final 9/9 are separately recorded.

Rider frontend cleanup remains unsupported; see BASELINE.md. No engine/contracts/PRNG changed.
