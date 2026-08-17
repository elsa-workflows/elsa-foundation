# Activities Design Compatibility Evidence Contract

## Baseline order

The historical FastEndpoints capture commit MUST precede production migration. Its runner is self-contained and
clean-content guarded; the receipt stores branch-durable source tree/build-input identities rather than requiring
an unreachable commit after squash merge.

## HTTP evidence

For every registration, compare method/path, binding and precedence, status, body bytes/JSON semantics, response
headers, content type, redirects/challenges, ProblemDetails/domain errors, cancellation, and concurrency behavior.
The corpus includes all 38 anonymous challenges and meaningful authenticated cases across every endpoint family.

## OpenAPI evidence

Consume all 38 operations: operation ID, Activities Design tag/owner, parameters, request body, response statuses,
schemas, headers, and security. Every API-visible request/response type resolves through the stable API Core and
owner JSON resolver chain before any reflection fallback.

## Approvals

Approvals are exact typed facets with real before/after values. Duplicate, unused, no-op, one-sided, unknown,
wrong-value, and stale route/component/document approvals MUST fail with deterministic diagnostics. A deliberate
mutation for each rejection class proves the comparer bites.
