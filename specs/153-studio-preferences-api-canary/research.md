# Research: Studio Preferences API Canary

## CShells module mapping seam

**Decision**: Implement `CShells.AspNetCore.Features.IWebShellFeature` on `StudioPreferencesApiFeature` and delegate its `MapEndpoints(IEndpointRouteBuilder, IHostEnvironment?)` method to a module-owned `MapStudioPreferencesApi` mapper.

**Rationale**: CShells already discovers enabled `IWebShellFeature` implementations per shell generation, supplies a shell-scoped route builder/service provider, adds shell metadata and prefixes, publishes the resulting endpoints, and removes them on lifecycle changes. This is the exact explicit mapping hook required by ADR 0068 and avoids a new Elsa registry or host-specific call.

**Alternatives rejected**: Calling the mapper directly from `Elsa.Workbench` would make one application root own a reusable module and would not work for feed-loaded shells. Adding a new mapper interface or DI collection would duplicate the existing CShells seam. Retaining FastEndpoints discovery would not satisfy the program goal.

## Permission policy composition

**Decision**: Protect GET with `RequireAnyPermission("*", StudioPreferencesPermissions.Read)` and PUT with `RequireAnyPermission("*", StudioPreferencesPermissions.Write)`.

**Rationale**: Existing Elsa FastEndpoints bases create one canonical Foundation Identity `Any` policy containing the administrative wildcard plus the action permission. The shared evaluator treats `*` as an explicit grant, expands `write -> read`, validates normalized principals, and runs resource handlers. Reusing the same policy codec preserves 401/403/exact/implied/wildcard behavior.

**Alternatives rejected**: `RequirePermission(Read/Write)` alone would drop existing wildcard access. Two separate authorization policies would compose as AND. Direct claim matching or path middleware would bypass Foundation Identity.

## Binding and scope authority

**Decision**: Bind the route namespace independently from the JSON write payload, and pass the route value to `StudioPreferenceScopeResolver`.

**Rationale**: The existing FastEndpoints request DTO combines a route-bound `Namespace` property with body fields. In Minimal APIs the route is the authoritative scope selector; a body value must not override it. Subject and tenant remain session-derived and the Studio host remains header-derived.

**Alternatives rejected**: Trusting a body `namespace` would allow scope confusion. A custom general-purpose binder would add an abstraction for one endpoint. Moving scope resolution into middleware would hide authorization-relevant inputs.

## Error and ProblemDetails preservation

**Decision**: Capture the legacy host first, then implement module-local exception-to-result translation that exactly matches the observed status, content type, body, and ProblemDetails facets.

**Rationale**: FastEndpoints' global ProblemDetails configurator and `ThrowError` behavior are observable contracts. Minimal API convenience results do not automatically guarantee the same representation. The shared compatibility harness makes the required shape measurable.

**Alternatives rejected**: Assuming `Results.BadRequest` or `Results.Problem` is equivalent risks contract drift. Adding a shared Elsa error framework before three consumers establish a common need would violate the ADR's bounded-convention rule.

## HTTP and OpenAPI evidence

**Decision**: Commit canonical FastEndpoints-before evidence for representative GET/PUT cases and the consumed OpenAPI operations, then compare the Minimal-API-after capture using the existing compatibility comparer and exact approvals model.

**Rationale**: A migration must prove compatibility against the implementation it replaces. TestServer can host the current feature, Foundation authorization, and an OpenAPI document endpoint before production code is removed. Canonical projections avoid unstable whole-document noise.

**Alternatives rejected**: Reconstructing expected behavior after migration is circular. Source-only route inspection misses binding and response behavior. Auto-updating snapshots makes failures self-accepting.

## Coexistence and transition removal

**Decision**: Keep one unrelated FastEndpoints route in the test host while Studio Preferences maps through Minimal APIs, and delete the two Studio Preferences entries from the transition registry.

**Rationale**: The host must remain deployable during bounded migration waves, but the migrated module must have exactly one authoring path. A mixed host verifies middleware ordering, shell-scoped services, and shared authorization without leaving duplicate production routes.

**Alternatives rejected**: Running only a Minimal API host would not prove the migration mechanism. Keeping the old Studio endpoints disabled or hidden would preserve process-global registration and transition debt.

## Collectible-context evidence

**Decision**: Verify repeated route and service-graph release using the existing weak-reference harness, with a production-assembly collectible fixture where runtime loading permits it. Treat serializer-cache retention as a separately classified stage.

**Rationale**: Weak references after releasing all owned references are the required evidence; process memory is not. The program harness already distinguishes routing, DI, serializer, and harness retention, so the canary should reuse those semantics.

**Alternatives rejected**: A single process-memory observation cannot identify owners. Claiming collectibility from the choice of Minimal APIs alone is unsupported. Folding atomic shell-generation publication into this issue would duplicate #1345.
