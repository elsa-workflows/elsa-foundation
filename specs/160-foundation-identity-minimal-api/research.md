# Research: Foundation Identity Minimal API Migration

## Endpoint authoring boundary

- **Decision**: Map the nine routes explicitly from their two owning feature packages through the existing web-shell feature seam.
- **Rationale**: This keeps identity semantics in the owner while exposing standard ASP.NET Core endpoints that can coexist with retained FastEndpoints routes.
- **Alternatives considered**: A shared endpoint DSL would recreate framework coupling; migrating unrelated routes would break the wave's bounded rollback surface.

## Token trust boundary

- **Decision**: Establish token-exchange principals only through configured interactive authentication schemes and never reuse a default-scheme bearer principal.
- **Rationale**: The prior endpoint explicitly constrained schemes. Reusing an existing first-party bearer would turn the token endpoint into a re-minting path.
- **Alternatives considered**: Checking only `IsAuthenticated` is insufficient because OpenIddict validation can populate the principal before the handler.

## Permission ownership

- **Decision**: Attach only `identity.providers.read` to capabilities; keep implication, wildcard, and normalized external claims in Foundation's evaluator pipeline.
- **Rationale**: Endpoint metadata states the action the module owns. Grant compatibility is evaluator behavior, not a second route capability.
- **Alternatives considered**: An any-policy containing wildcard obscures catalog ownership and diverges from the program convention.

## Contract evidence

- **Decision**: Preserve immutable real-FastEndpoints HTTP/OpenAPI fixtures and require exact approvals for known authoring-framework differences.
- **Rationale**: Hand-picked assertions cannot prove nine-route compatibility or detect unused/broadened approvals.
- **Alternatives considered**: Metadata-only tests do not execute authentication, serializers, challenges, cookies, or redirects.

## Serialization and unloading

- **Decision**: Use owner-local source-generated JSON contexts and repeated owner-isolated weak-reference tests that materialize routes, auth schemes/provider delegates, DI, serialization, and disposal.
- **Rationale**: Reflection metadata and framework caches can retain collectible assemblies even when route registration appears correct.
- **Alternatives considered**: Process-memory observations or static mapper tests are not evidence of collectibility.
