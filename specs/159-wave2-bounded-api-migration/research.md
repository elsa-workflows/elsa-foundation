# Wave 2 Migration Research

## Findings

1. The transition inventory contains exactly 13 target registrations: three BPMN interchange routes, two module-management routes, three execution-evidence routes, and five Elsa 3 import routes. Their immutable before evidence is captured by a real TestServer using `MapFastEndpoints` and the real OpenAPI endpoint.
2. The four production features currently derive from `FastEndpointsFeatureBase`; each already owns the domain services needed by a direct mapper. The migration therefore changes composition and HTTP binding, not the domain contracts.
3. Foundation Identity's canonical endpoint policy is a catalog-owned action permission. The evaluator expands an authenticated wildcard grant implicitly, so endpoint mappings must use `RequirePermission(action)` and never encode wildcard in an any-permission policy.
4. `Elsa.Api.Compatibility.Testing` captures request binding, status, media type, headers, body, ProblemDetails, paging, and bounded streaming. The before fixture includes anonymous rejection plus authenticated deterministic success and error cases for every contract family.
5. CShells `IWebShellFeature` is the module composition seam. The migrated features must keep their current service registrations, expose an explicit `MapEndpoints(IEndpointRouteBuilder)` method, and map only their own routes.
6. The Elsa 3 upload handler consumes the raw request stream while clients send multipart content. Its mapper must preserve the raw stream, content length, 201/location response, scoped identity, and existing error mapping. Execution evidence must preserve query polling and plain-text validation errors.
7. Collectibility requires materialized endpoint, DI, serializer, and disposal evidence for each owner. Tests must retain weak references and scalar diagnostics only after releasing routes, service providers, OpenAPI documents, and request delegates.

## Decisions

- Keep all new mappers module-local; no shared endpoint DSL or broad HTTP abstraction is introduced.
- Preserve existing public request/response records and service interfaces where possible. Use explicit `HttpContext` binding only where the old endpoint consumed raw streams/query values.
- Add one permission contributor for Execution Evidence. Its read permission is used by both reads; delete is used by the delete route; manage implies delete and read. Modularity reuses its existing contributor and implication.
- Capture and commit before fixtures before deleting any endpoint or FastEndpoints dependency. After migration, compare the real host through the compatibility library and allow only reviewed metadata/media-type differences required to correct the old upload documentation.
- Remove exactly the 13 transition entries after Wave 1 rebase; do not edit unrelated inventory entries.

## Open Questions Resolved

- Wildcard is not an endpoint permission operand. It is proven by the Foundation evaluator in authorization tests.
- The current nightly integration issue #1323 is an external main-branch failure and is not evidence of a Wave 2 owner regression.
