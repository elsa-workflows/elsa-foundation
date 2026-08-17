# Implementation Plan: OpenTelemetry Minimal API migration

**Branch**: `codex/1371-wave5-opentelemetry-minimal-apis` | **Spec**: [spec.md](spec.md)

## Summary

Migrate the eight diagnostics query/SSE shell routes and reconcile the three retained OTLP routes to explicit owner-owned Minimal API mapping. Preserve the real FastEndpoints HTTP/OpenAPI oracle, transport authentication-before-body-read, source-generated JSON, stable endpoint metadata, and unloadability.

## Technical context

- **Runtime**: C# / .NET 10 ASP.NET Core Minimal APIs.
- **Owner**: `Elsa.Diagnostics.OpenTelemetry`; core contracts remain in `Elsa.Diagnostics.OpenTelemetry.Core`.
- **Authorization**: catalog contributor owns `Diagnostics:OpenTelemetry.Read`; wildcard evaluation remains evaluator-level. OTLP uses API-key/loopback transport authentication and anonymous endpoint metadata.
- **Serialization**: owner `JsonSerializerContext` for JSON DTOs; protobuf remains the dependency-free OTLP parser.
- **Verification**: immutable FE-before fixture/oracle, real-host HTTP/OpenAPI comparer, authorization matrix, repeated collectible owner cycles, owner tests, architecture/maps/build/format checks.

## Design

`OpenTelemetryFeature` implements `IWebShellFeature` with virtual service configuration and endpoint mapping. `OpenTelemetryApi` maps query and SSE routes with explicit owner/authoring/name/tag/security metadata and typed JSON binders/results. The root OTLP mapper remains the single three-route collector surface and uses static request delegates so OpenAPI metadata is emitted without retaining collectible owners. The owner-local SSE writer removes the production FastEndpoints dependency.

## Constitution check

Pass: exactly 11 transition registrations are removed; protocol ownership and OTLP transport authentication stay unchanged; endpoint metadata names one catalog action; source-generated serialization and repeated unload evidence are present; no external/network provider is required.

## Structure

Production changes live under `src/Elsa/Diagnostics/OpenTelemetry`; behavior/compatibility tests live under `tests/Elsa/Diagnostics/OpenTelemetry/Tests`; collectibility and ratchet checks live under `tests/Elsa/Architecture`; immutable evidence and work-unit artifacts live under `tests/.../Baselines` and `specs/162-opentelemetry-api-migration`.
