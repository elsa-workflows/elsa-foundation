# Wave 1 Migration Evidence Model

## Owner mapper

- `OwnerId`: stable assembly owner string.
- `MapEndpoints`: CShells web-feature seam.
- `Map*Api`: explicit `IEndpointRouteBuilder` entry point usable outside CShells.

## Endpoint contract observation

- route template and method;
- route/query/header/body binding;
- JSON property names and response body type;
- success and error status;
- response content type and ProblemDetails/plain-text shape;
- consumed OpenAPI operation and response metadata;
- permission policy and catalog owner.

## Lifecycle evidence

Each owner cycle records route publication, request execution, service disposal, serializer release, and weak-reference collection. The evidence is diagnostic only and never replaces a failed collection check with aggregate memory measurements.
