# Implementation Plan: React Admin Module Host

**Branch**: `codex/react-admin-module-host` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)

## Summary

Add a modular React admin shell mounted at `/admin`, with server-discovered ESM admin modules and two proving samples. Preserve the temporary demo client at `/` and add `/demo` as an alias while moving its source into `DemoClient`.

## Technical Context

**Language/Version**: C# net10.0; TypeScript/React 19; Vite; npm.

**Primary Dependencies**: CShells features, Elsa Events, Elsa FastEndpoints, ASP.NET static web assets, React, React DOM, Vite, Vitest.

**Storage**: N/A.

**Testing**: xUnit for server contracts and manifest collection; Vitest for admin SDK/loader/sample module behavior.

**Target Platform**: ASP.NET server plus modern browser with import map support.

**Project Type**: ASP.NET-hosted modular web app.

**Performance Goals**: Admin shell starts with startup-discovered modules and isolates failed module activation.

**Constraints**: Same-origin trusted modules only; startup-time discovery only; preserve existing demo routing.

**Scale/Scope**: First slice with base shell plus two sample modules.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Three-layer separation | PASS | Admin contracts, API, web host, and samples are split by responsibility. |
| Event contribution model | PASS | Modules contribute manifests through a named event. |
| Provider dependency envelope | PASS | Frontend dependencies stay in admin web/sample packages. |
| Design vs runtime split | PASS | Admin UI is not part of runtime execution contracts. |
| Unit tests for feature/service behavior | PASS | Server and frontend tests cover new logic-bearing services. |

## Project Structure

```text
src/Elsa/Admin/Core/
src/Elsa/Admin/Api/
src/Elsa/Admin/Web/
src/Elsa/Admin/Samples/Dashboard/
src/Elsa/Admin/Samples/WeatherForecast/
src/Apps/Elsa.Server/DemoClient/
tests/Elsa/Admin/Tests/
specs/071-react-admin-module-host/
```

**Structure Decision**: Use Elsa package boundaries for server contracts/API/web assets and sample modules. Keep the temporary demo app under the server app because it remains demo-only.

## Implementation Steps

1. Create Speckit artifacts and update active Speckit pointer.
2. Move the demo client source and update the server project build target.
3. Add admin core manifest contracts and extension-point catalog.
4. Add admin API feature, manifest provider, and endpoint.
5. Add admin web feature, route mapping, React shell, SDK, loader, diagnostics, and static asset build.
6. Add dashboard sample module.
7. Add weather forecast sample module and deterministic endpoint.
8. Wire features into `Elsa.Server` and mount `/admin` plus `/demo`.
9. Add server and frontend tests.
10. Build/test, refresh maps, and commit.

## Risks

- Static web asset paths must be stable enough for module manifests.
- Import-map-based dependency sharing needs browser support and deterministic asset paths.
- Demo fallback routing must not swallow `/admin` paths.
