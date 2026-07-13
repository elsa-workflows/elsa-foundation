# Expressions API

`Elsa.Expressions.Api` is the supported management-client projection of expression descriptors and selectable variable types. It exposes design-time metadata only; expression evaluation remains in the Expressions domain runtime services.

## Composition

Add `ExpressionsApiFeature` to the active shell. It depends on the `Expressions` feature, whose registries aggregate descriptor providers. Expression-language modules contribute descriptors through the core provider seams; the API does not maintain a second descriptor catalog.

The package has no dependency on `Elsa.Server` and can be composed directly by a custom host.

## Supported routes and authorization

- `GET /expressions/descriptors`
- `GET /expressions/variable-types`

Both routes require `expressions.read` or the shared wildcard permission. Authentication and RFC 7807 errors use the common FastEndpoints API infrastructure.

## Extension points

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md). Canonical API ownership is in the [domain-owned API spec](../../../../specs/092-domain-owned-apis/spec.md), and shared terminology is in the [Elsa glossary](../../../../docs/glossary/elsa.md).
