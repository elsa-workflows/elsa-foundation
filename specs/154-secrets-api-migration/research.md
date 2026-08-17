# Research: Secrets API Minimal API Migration

## Authoritative surface and stale documentation

**Decision**: Treat the current production registrations and captured real-host behavior as the migration baseline. In particular, preserve `PUT /secrets/{name}` even though the older Secrets contract describes a POST update.

**Rationale**: The goal is framework replacement without public redesign. Generated clients and running hosts observe code, not the stale planning document. A real before capture makes this discrepancy explicit and reviewable.

**Alternatives rejected**: Changing to the older documented method would be an unapproved public contract change. Treating both methods as valid would add a route and obscure ownership.

## CShells module mapping seam

**Decision**: Implement `IWebShellFeature` on `SecretsApiFeature` and delegate to a module-owned `MapSecretsApi(IEndpointRouteBuilder)` entry point.

**Rationale**: CShells already discovers the enabled feature per generation, supplies shell-scoped services and prefixes, enriches ownership metadata, publishes endpoints, and removes generation-owned data sources. This is the explicit standard seam accepted by ADR 0068.

**Alternatives rejected**: Application-root mapping would not compose feed-loaded shells. A new mapper registry would duplicate CShells. Retaining FastEndpoints discovery would not deliver the program outcome.

## Collectible-safe handler boundary

**Decision**: Use explicit `RequestDelegate` handlers with `HttpContext` resolution and manual web-JSON binding, then describe inputs and outputs with endpoint metadata.

**Rationale**: The Studio Preferences canary proved that materializing typed Minimal API handlers can populate process-wide `RequestDelegateFactory` caches with collectible parameter types. Explicit request delegates preserve standard endpoints while avoiding that ownership leak.

**Alternatives rejected**: Typed delegates are terser but fail the proven dynamic lifecycle. Reflection-only route counting would make tests green without modeling publication. A new generic endpoint framework would violate the bounded-convention decision.

## Permission ownership and implication

**Decision**: Contribute all eight existing Secrets permission constants from `Elsa.Secrets.Api`. Preserve wildcard-or-action policies on endpoints and declare only `secrets:write` as implying `secrets:read`.

**Rationale**: Five permissions protect current routes; use/import/export are already public stable names and need one discoverable owner. Write-to-read matches the program canary and operator expectations. Keeping all other actions independent preserves least privilege and avoids silently granting metadata access from rotate/delete/test authority.

**Alternatives rejected**: Cataloging only used routes leaves stable module permissions ownerless. Adding a broad manage permission or full implication lattice changes the public permission vocabulary and expands access. Direct claim checks bypass normalized principals, replaceable evaluators, and resource handlers.

## Tenant authority and descriptor exception

**Decision**: Read `IdentityClaimTypes.TenantId` for every data operation, return the captured forbidden result when absent, and never accept tenant identity from route, body, or headers. Keep descriptor discovery tenant-independent.

**Rationale**: Current handlers fail closed and existing tests pin forwarding. Descriptors enumerate safe global capabilities and currently do not resolve a tenant; adding a tenant check would drift behavior.

**Alternatives rejected**: A tenant request field enables scope confusion. A path middleware rule would hide an endpoint-specific exception and couple authorization to routes.

## Binding authority

**Decision**: Route `name` is the only target identity for update, rotate, revoke, delete, and test. Bind create/list/picker inputs exactly as observed and preserve ASP.NET web JSON camel-case names and string enums.

**Rationale**: Legacy request DTOs combine route and body members, but the route and normalized tenant are the security boundary. Explicit binding prevents a body property from overriding scope while retaining the wire model.

**Alternatives rejected**: Generic reflection binding would recreate framework machinery. Trusting a body name risks target confusion. Renaming or reshaping inputs belongs to a versioned contract change.

## Error and safe-result preservation

**Decision**: Capture domain validation, duplicate/conflict, not-found, malformed JSON, and global ProblemDetails behavior before replacement, then implement only the translation required for exact parity. Preserve test failures as HTTP 200 safe result documents.

**Rationale**: Current explicit 403/404/204 branches and uncaught domain exceptions produce observable framework behavior. Minimal API convenience results cannot be assumed equivalent.

**Alternatives rejected**: Improving errors during migration conflates architecture and contract change. A shared error framework lacks evidence from multiple migrated modules.

## Sensitive-data evidence

**Decision**: Seed unique value/configuration/provider markers and assert they never appear in response bodies, headers, errors, OpenAPI response schemas/examples, or audit output.

**Rationale**: Existing models and mappers intend metadata-only responses, but no current HTTP test proves serialization and global error paths respect the boundary. This is the representative security proof.

**Alternatives rejected**: Type inspection misses error text and serializer behavior. Checking only successful get/list responses misses create/rotate failures and provider diagnostics.

## HTTP and OpenAPI compatibility

**Decision**: Commit immutable FastEndpoints-before HTTP observations and a canonical projection from the actual ASP.NET Core OpenAPI document, then compare the replacement using the shared exact-difference model.

**Rationale**: Real before evidence avoids circular reconstruction. Operation-level projections cover all ten consumed endpoints while excluding unrelated whole-document ordering noise.

**Alternatives rejected**: Hand-written before documents, Minimal-generated expectations, or automatic snapshot acceptance cannot prove preservation.

## Coexistence and transition retirement

**Decision**: Keep one unrelated secured FastEndpoints route in the test host while the production Secrets module maps only Minimal APIs. Remove all ten Secrets transition entries and the production FastEndpoints dependency after replacement tests pass.

**Rationale**: Hosts remain deployable throughout the program, but a migrated module must have one unambiguous authoring path. The mixed host verifies shared middleware and evaluator behavior.

**Alternatives rejected**: A Minimal-only fixture does not prove transition compatibility. Keeping disabled legacy classes preserves discovery debt and makes source reconciliation ambiguous.

## Exercised unload evidence

**Decision**: Materialize routes, execute representative query/body traffic, generate the consumed OpenAPI projection, then release route, service, serializer/documentation, and harness owners before bounded weak-reference verification over repeated cycles.

**Rationale**: This addresses the canary's remaining risk: mapping-only evidence did not exercise JSON and documentation generation. Stage-specific diagnostics are more honest than process-memory observations.

**Alternatives rejected**: Host disposal alone is not proof. Treating framework caches as an automatic exception would weaken the program's dynamic-module requirement.
