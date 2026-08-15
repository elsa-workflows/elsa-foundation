# Data Model: REST API Migration Compatibility and Authoring Gates

## Endpoint ownership metadata

- `Owner`: stable first-party module or host identifier; required and non-empty.
- Applied once to each enabled endpoint. Conflicting values are invalid.

## Security disposition metadata

- `Kind`: `Permission`, `Public`, `HostCredential`, or `NamedPolicy`.
- `Value`: permission or policy name when required; absent for public access.
- `Owner`: owner of a named policy or host-credential scheme when applicable.
- Exactly one primary disposition is valid. Foundation permission metadata supplies the `Permission` form.

## Endpoint manifest entry

- `Route`: normalized route pattern.
- `Methods`: normalized, sorted HTTP methods.
- `DisplayName`: diagnostic runtime identity.
- `Owner`: endpoint owner.
- `AuthoringModel`: Minimal API, MVC, FastEndpoints, or another explicitly known source.
- `SecurityDisposition`: one validated primary disposition.
- `ContentTypes`: sorted declared request/response media types.
- `Responses`: sorted status, body type, and content metadata relevant to compatibility.
- `SourceIdentity`: optional diagnostic file/type/feature identity; excluded from semantic route equality where appropriate.

Entries sort by route, method, owner, and stable source identity. Equivalent parameter names normalize to the same template shape while diagnostics retain the original pattern.

## HTTP observation

- `EndpointKey`: normalized route and method.
- `Case`: stable scenario name.
- `Request`: binding inputs relevant to the case.
- `StatusCode`, `ContentType`, `Headers`: consumed response protocol facts.
- `Body`: canonical JSON, ProblemDetails, paging/filtering result, or bounded stream frames.
- `TerminalState`: completion, cancellation, or expected stream termination.

## OpenAPI observation

- `EndpointKey`.
- `Operation`: consumed parameters, request-body requirements, responses, media types, and schema projection.
- Documentation-only noise that consumers do not observe is excluded by a deterministic canonicalizer, never by broad text ignores.

## Compatibility delta

- `EndpointKey`, `Case`, `Facet`.
- `Expected`, `Actual`: canonical scalar or structured values.
- Deltas sort by key and facet.

## Approved difference

- Exact `EndpointKey`, `Case`, and `Facet`.
- Exact expected and actual canonical values.
- `Owner`, `Reason`, and `FollowUp`.
- A record is invalid when any scope field is empty, duplicated, or broader than one comparison facet.

## Transition exception

- `RegistrationIdentity`: endpoint type or exact owned registration identity.
- `Owner`: feature/module responsible for the surface.
- `Routes` and `Methods`: exact normalized scope.
- `RemovalOwner` and `FollowUp`: accountable migration record.
- `DynamicallyUnloadable`: must be false.

Every discovered FastEndpoints registration must match exactly one record; every record must match a discovered registration. Additions, stale records, ambiguous matches, and dynamic-module records fail.

## Permission ownership record

- `Permission`: exact non-wildcard name.
- `CatalogOwner`: provenance emitted by the active permission catalog.
- `EndpointConsumers`: enabled endpoint keys and their route owners.

The permission must have one catalog owner. Consumers may have different route owners. Missing or duplicate catalog owners and inconsistent endpoint dispositions fail.

## Unload cycle evidence

- `Cycle`, `Stage`: route, services, serializer, or clean.
- Weak references to the load context and fixture assembly/type.
- `Collected`: bounded final state.
- `CollectionAttempts`: count and lifecycle observations.
- `Diagnostic`: classification of deliberate or unexpected retention.

The evidence object must not retain a strong reference to the collectible context or any loaded object.
