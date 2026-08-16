# Wave 5 OpenTelemetry Minimal API evidence

Issue #1371 migrates exactly eleven shell registrations: seven query routes, one SSE stream, and three OTLP FastEndpoints adapters. The existing three-route root OTLP mapper remains the only collector route implementation.

## Immutable before oracle

The first branch commit, `db01e3ec1`, contains only the real-host FastEndpoints capture and oracle test, captured from parent `c04d8dbbe`. Fixture SHA-256: `d3066a8c8c0eacbdd9409a340b2587c07394336088ae9449ddeb3d7e0be291cc`. It contains eleven HTTP cases and the consumed OpenAPI document for all eleven routes.

## After comparison

`OpenTelemetryCompatibilityTests` uses one real TestServer client and compares all eleven methods/routes, statuses, response bodies/content types, redirect `Location` headers, and consumed OpenAPI paths, request bodies, operation IDs, tags, and response status sets. Stable operation IDs and `OpenTelemetry` tags are asserted for every migrated and retained route. The exact registry records the three retained root OTLP 204→200 status deltas, the trace 404 metadata addition, the stream 204→200 metadata change, and the eleven explicit operation identity/tag changes; registry SHA-256: `de7db504afecec2f9703c052d3c4b02d89be3fc6d3ac4f9e4d66b5510171a781`.

## Security and lifecycle evidence

`OpenTelemetryAuthorizationTests` covers anonymous 401, authenticated lacking 403, exact action, wildcard evaluator grant, non-implied parent action, and tenant/resource-bearing normalized principals. OTLP tests retain API-key/loopback authentication before body reading. The owner-local SSE writer exercises cancellation and disposal. `Wave5OpenTelemetryMinimalApiCollectibilityTests` repeats three collectible owner cycles, mapping all eleven routes, materializing provider/live-feed/authenticator/binder/serializer services, executing query/SSE/loopback protobuf delegates, disposing routes/DI, and verifying weak-reference unload.

The catalog-owned endpoint action is `Diagnostics:OpenTelemetry.Read`; wildcard is not endpoint metadata. The legacy `Diagnostics:OpenTelemetry` grant remains a catalog alias implying Read so existing host assignments continue to work. OTLP remains transport-authenticated and anonymous at the ASP.NET endpoint policy layer.
