# Wave 5 OpenTelemetry Minimal API evidence

Issue #1371 migrates exactly eleven published registrations: seven query routes, one SSE stream, and three OTLP signal routes. `OpenTelemetryFeature.MapEndpoints` is the shell lifecycle mapper and invokes the retained three-route `MapOpenTelemetryOtlpReceiver` exactly once. Hosts that do not compose the shell feature may call that retained mapper directly; no FastEndpoints OTLP adapter is reintroduced.

## Immutable before oracle

Commit `db01e3ec1` contains the real-host FastEndpoints capture and oracle test, captured from parent `c04d8dbbe`. The main fixture SHA-256 is `5b703d9f4c03303113af1a1c52d04acd748d147f738923e2b942119afe374164`; it contains eleven HTTP cases (including exact cookie redirect locations) and the consumed OpenAPI document for all eleven routes. The authenticated and binding supplements are separate correction fixtures, captured against the deleted FastEndpoints implementation rather than presented as baseline-first evidence: `otel-http-authenticated-fastendpoints.json` SHA-256 `4a698e7bbd9361892951d6e3715e039dd49f4821271b5ad998e3135ae92c9e5d`, and `otel-http-binding-fastendpoints.json` SHA-256 `4dddf5e22726d31f698465b7b56e86b1b30ba61f58755c00c6146754abeb2088`.

## After comparison

`OpenTelemetryCompatibilityTests` uses one real TestServer client and compares all eleven methods/routes, statuses, response bodies/content types, redirect `Location` headers, and consumed OpenAPI paths and methods. It normalizes only registry-approved operation IDs/tags, response status additions/removals, the three source-generated SSE schemas, and the document tag; every remaining parameter, request body, security declaration, response content/schema/header/description, component, and document field must match byte-equivalently after JSON normalization. The registry validates both before and after values and rejects stale or unused approvals. Registry SHA-256: `efceedd73b0c53cb4cf5c84e65872e52d7fb49120d8fe7a314d82b1e2189c6ca`.

## Security and lifecycle evidence

`OpenTelemetryAuthorizationBoundaryTests` exercises anonymous 401, authenticated lacking 403, exact action, legacy implication, wildcard evaluator grant, non-implied parent action, untrusted and invalid normalized identities, ambiguous identities, and resource/tenant allow-deny decisions through both Minimal API and the retained FastEndpoints canary. `OpenTelemetryMappedOtlpAuthenticationTests` exercises the real mapped `DefaultOtlpRequestAuthenticator` for valid/invalid API keys, loopback and external callers, typed HostCredential metadata, and a throwing body stream proving rejected requests read zero bytes. The owner-local SSE writer proves real frames, flush/backpressure behavior, completion, cancellation, and pending enumerator disposal. `Wave5OpenTelemetryMinimalApiCollectibilityTests` repeats three collectible owner cycles, maps all eleven routes through the feature lifecycle, executes the query binder, typed source-generated serializer, provider, all three protobuf delegates, default transport authenticator, completed and cancelled SSE streams, DI disposal, and weak-reference unload.

The catalog-owned endpoint action is `Diagnostics:OpenTelemetry.Read`; wildcard is not endpoint metadata. The legacy `Diagnostics:OpenTelemetry` grant remains a catalog alias implying Read so existing host assignments continue to work. Query and SSE routes use Foundation Identity policy authorization. OTLP routes are `AllowAnonymous` at the framework policy layer but publish typed `HostCredential` security disposition metadata and perform API-key/loopback transport authentication before any body read.

## Gate status

The implementation and evidence gates are complete except for the final integration bookkeeping in Spec Kit task T010. The branch is based on the current Wave 3 integration point; the parent integration pass must rebase after Wave 4 and ratchet the shared transition count from `123 → 112` across four remaining owners. No production migration beyond the eleven OpenTelemetry registrations is included.
