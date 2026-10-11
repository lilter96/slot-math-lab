# Proposal: an explicit bound for the existing Fold expression

Status: proposed only; not implemented or authorized.

The current expression budget assumes up to 64 elements for each collection
iteration. A nested Fold for a bounded traversal therefore pays the product of
those bounds even when its authored sources contain only 30 positions and four
directions. The attempted traversal exceeded the existing 10,000-operation
budget. Raising or bypassing that budget is not part of this proposal.

The smallest proposed capability is an optional `maxItems` assertion on Fold.
It is a nonnegative integer upper bound on the source length. Before entering
the body, every interpreter checks the actual source length and reports a located
collection-limit error if it exceeds the assertion. It never truncates a source.
An omitted assertion preserves the current contract. Validation uses the asserted
bound when estimating expression cost; all other budget limits remain unchanged.

This would allow bounded traversals to be composed from existing Fold, If,
index and append expressions with fewer state-update nodes. The mechanics stay
in editable JSON. Neither the compiler nor validation would recognize games,
mechanics, transform identifiers or authored traversal patterns. A traversal can
be split into smaller bounded folds if its expression still exceeds the budget.

Required independent tests cover empty arrays, the boundary and boundary plus
one; located errors; nested and shadowed bindings; lazy predicates; cancellation
and state restoration; and equality across Exact, reference and sampling tiers.
Schema and frontend authoring must describe the assertion explicitly.

Any speed improvement must be measured with a prototype after authorization.
The current measurements do not establish a predicted gain or one-million-round
throughput for Nine Lives. Explicit user approval is required before implementation.
