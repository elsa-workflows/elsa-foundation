# Implementation Plan: Retained Host Route Ownership and Security Metadata

**Branch**: `codex/1365-host-route-metadata` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Annotate all retained Workbench and Foundation Host routes with shared typed host ownership, Minimal API authoring,
and exactly one security disposition. Add a marker for custom management-key endpoint filters so the deterministic
manifest validates those routes without introducing an authentication scheme. Chain the same server-side key filter
onto Workbench's previously unguarded CShells management mapping. Preserve health, CORS, SignalR, trusted-caller,
and existing module-management behavior with focused tests.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs, CShells Management API, ConsoleLogStreaming, Elsa.Api.AspNetCore metadata

**Storage**: N/A; route metadata and manifests are in-memory/test artifacts

**Testing**: xUnit, ASP.NET Core TestHost/in-process endpoint data sources, architecture guards, generated map check

**Target Platform**: ASP.NET Core Workbench and Foundation Host

**Project Type**: Multi-project .NET web host and reusable endpoint metadata library

**Performance Goals**: Metadata publication adds no request-path work beyond existing endpoint conventions/filters

**Constraints**: Preserve existing HTTP/JSON, CORS, SignalR, health, management-key, and trusted-caller behavior;
do not invent Foundation user permissions for host-control routes.

**Scale/Scope**: 67 semantic retained root-host surfaces: Workbench 63 (including one conceptual SignalR hub),
Foundation Host 4. The ASP.NET Core endpoint manifest publishes 68 physical entries because the SignalR hub also
has a negotiate endpoint; the focused Workbench manifest fixture therefore expects 64 entries when console streaming
is enabled.

## Constitution Check

- Preserve refactor behavior and existing tests; no public route redesign.
- Keep shared endpoint conventions small and standard ASP.NET Core based; no replacement endpoint framework.
- Keep host-control credentials server-side per ADR 0037; do not move claim matching into endpoint mapping.
- Add tests before implementation and retain architecture/map/diff gates.

## Project Structure

```text
src/Elsa/Api/AspNetCore/                         # shared metadata and convention marker
src/Elsa/Modularity/ExtensionBuilder/            # Extension Builder host route metadata
src/Apps/Elsa.Workbench/                         # Workbench root/module/health/CShell routes
src/Apps/Elsa.Foundation.Host/                   # Foundation Host root routes
tests/Elsa/Api/Compatibility/Testing/Tests/      # manifest validation tests
tests/Elsa/Modularity/Tests/                      # Workbench route manifest/security tests
tests/Elsa/Architecture/                         # source-level host route guards
```

**Structure Decision**: Keep the shared contract in `Elsa.Api.AspNetCore`; annotate mappings where they are owned;
keep tests in the existing API compatibility, modularity, and architecture suites.

## Phase 0 Research

Completed in [research.md](research.md): shared metadata/manifest reuse, custom filter marker, ADR 0037 key
boundary, and default-policy treatment for console-log streaming.

## Phase 1 Design

Completed in [data-model.md](data-model.md) and [contracts/retained-host-endpoint-manifest.md](contracts/retained-host-endpoint-manifest.md).

## Constitution Check (post-design)

Pass. The design adds one small inspectable metadata marker and host-local mapping conventions; it does not add a
framework abstraction, alter public routes, or substitute user permissions for host credentials.
