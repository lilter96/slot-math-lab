# Practitioner review kit

Status: prepared for real review, not completed user acceptance. No participants have been contacted and no feedback or timings have been fabricated. This kit addresses UX_MASTERPLAN §§1–2 and DESIGN_SYSTEM §I.

## Participants and setup

Recruit at least one slot mathematician, one game backend developer and one math reviewer. Use at least five testers for the separate 4/5 discovery target; roles can overlap. Give each tester a fresh private QA account and an empty browser workspace, on an isolated review environment. Do not expose production records or credentials. Record source commit, browser, viewport and whether a task received assistance.

Provide a real reachable local/review URL and credentials separately. The task-owned local preview is http://127.0.0.1:4186 and API localhost:5166; these addresses work only on the host and are not published services. Server/backend ownership and feature flags must be verified before review. Nine Lives and Ancient Dragon are reference fixtures; example RTP is uncalibrated.

## Moderated tasks

1. From My projects, create a private copy of a game and identify where to edit its mathematics and run an evaluation. Start a timer on first usable render; stop when the participant correctly identifies both destinations. Do not point at navigation or treat an assisted answer as discovery success.
2. Open another game, describe its geometry and one supported mechanic. Switch back and demonstrate that the first project's edits remain. Duplicate the first project; change one copy and check the other stays independent.
3. Find free spins in Nine Lives, open its underlying mechanic and return. Explain which data can be safely edited in a form and which requires the graph.
4. Change a supported weighted draw, inspect the difference and validation, apply it, export/reload and undo where available. Explain what changed and what numerical evidence is still absent.
5. Select a graph node, focus an offscreen node, fit the selection, close/pin the inspector, and cancel then confirm a deliberate deletion. Attempt the workflow by keyboard. Use a throwaway project for destructive edits.
6. Inspect an actual completed owned run: identify method, seed, sample count, uncertainty and source model. Explain the difference between a configured target and measured RTP, and observed maximum versus a proven bound.
7. Execute the current model in Debug when enabled. Inspect returned state and explain any unsupported source attribution. Do not treat a coordinate diagram or reference demo as a generated current-model spin.
8. At phone width, find/create/open projects, inspect Overview/results, and identify the explicit advanced graph limitation. Simulate an unavailable backend and explain the recovery action.

Use an independently signed-in second account for isolation demonstrations; it should not see the first account's projects or private server evidence. Never attempt sharing: it is deferred.

## Record actual outcomes

For each participant record: anonymized role, source commit, viewport/browser, task, elapsed seconds where timed, success/failure, assistance, observed confusion, severity and suggested change. Include exact behavioral examples instead of general 'looks good' feedback. Record failures and abandoned tasks.

The discovery target passes only if at least four of five testers identify editing and evaluation within 30 seconds without assistance. Practitioner validation requires actual feedback from all three roles; this kit alone does not meet that gate.

For each major view, reviewers score 0–2 for task clarity, domain fidelity, trust, hierarchy, interaction, accessibility, design identity and integration. Record observations supporting each score. Minimum acceptance is 14/16 per view, with unresolved trust/security/math defects separately blocking release regardless of score. Attach actual screenshots at 1440×900, 1280×800, 1024×768 and 390×844, plus keyboard focus and unavailable-backend states. Automated/agent reviews must be labelled and cannot be presented as practitioner feedback.

## Follow-up

Convert observed failures into scoped changes and affected tests. Repeat only impacted tasks, retaining the initial record. Do not mark release ready until remaining Core/Rider/security/compatibility checks and user acceptance are complete. Hosting/deployment and contacting reviewers require their own authorization; preparation of this local kit does not imply either occurred.
