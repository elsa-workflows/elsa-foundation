# Data Model: Wave 6 Review Evidence

## RouteEvidenceCase

Represents one deterministic request against one design route.

- `Endpoint`, `Method`, and `Case`: stable identity.
- `Request`: path/query/headers/content type/body corpus.
- `Status`, `Headers`, `ContentType`, and `Body`: captured wire result.
- `SourceCommit` and `FixtureHash`: provenance for immutable before evidence.

## ConsumedOpenApiOperation

Represents the public operation consumed by a client comparison.

- `OperationId` and `Tag`: stable ownership-facing identity.
- `Path`, `Method`, and `Parameters`: route and binding contract.
- `RequestBody`, `Responses`, and `Security`: consumed schema contract.

## AuthorizationMatrixCase

Represents one principal/resource decision.

- `PrincipalKind`, `Claims`, `Tenant`, and `Resource`.
- `GrantKind`: exact, implied, wildcard, external-untrusted, or denied.
- `AuthoringModel` and `ExpectedStatus`.

## LifecycleGeneration

Represents one owner load/map/unload cycle.

- `GenerationId`, mapped delegate and metadata references.
- DI scope, stores/adapters, providers, authentication, OpenAPI, and serializer context.
- Disposal and weak-reference observations.
