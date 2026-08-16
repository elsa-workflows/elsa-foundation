# Compatibility Evidence Contract

## Inputs

- A named before evidence set and after evidence set.
- Canonical HTTP observations for shared cases.
- Canonical projections of supplied OpenAPI documents.
- An optional list of exact approved differences.

## Compared facets

- Route and method.
- Binding location, requiredness, names, and representative conversion behavior.
- JSON payload shape and serializer-visible values.
- Status codes, media types, and structured ProblemDetails.
- Paging and filtering request/response semantics.
- Bounded streaming media type, framing, cancellation, and terminal behavior.
- Consumed OpenAPI operation parameters, bodies, responses, media types, and schemas.

## Approved differences

An approval matches exactly one endpoint, method, case, facet, expected value, and actual value. It also names an owner, reason, and follow-up. Unused, duplicate, malformed, or overly broad approvals fail. No environment setting may auto-accept or rewrite evidence.

## Output

A stable ordered list of unapproved deltas and invalid/unused approvals. An empty list means the two authoring implementations are externally compatible for the captured surface; it does not assert business correctness beyond those cases.
