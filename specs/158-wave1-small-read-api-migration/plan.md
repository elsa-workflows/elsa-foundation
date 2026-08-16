# Implementation Plan: Wave 1 Small and Read-Oriented REST API Migration

**Branch**: `codex/1367-wave1-minimal-apis` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Replace the six Wave 1 FastEndpoints feature adapters with explicit `IWebShellFeature` implementations and module-owned `Map*Api(IEndpointRouteBuilder)` seams. Preserve the eight route contracts and service behavior, use Foundation Identity permission metadata for the existing action permissions and three new catalog-owned wildcard replacements, and remove the eight transition registrations and unused production FastEndpoints references.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, CShells `IWebShellFeature`, Foundation Identity authorization/catalog, existing Elsa services, xUnit, TestServer, compatibility manifest gates

**Storage**: Existing feature stores and services; no schema changes

**Testing**: Existing owner suites, new route/metadata/authorization contract tests, architecture transition ratchet, maps and full repository gates

**Target Platform**: Cross-platform ASP.NET Core hosts with static and collectible module composition

**Project Type**: Modular .NET library/API feature assemblies

**Performance Goals**: No request-time discovery or permission claim parsing; preserve existing response latency and streaming-free Wave 1 behavior

**Constraints**: Exact eight-registration scope; no public route redesign; no new endpoint DSL; no FastEndpoints dependency in migrated production owners; no weakening of authorization

**Scale/Scope**: Six owners, eight concrete registrations, three new catalog permissions, six mapping seams

## Constitution Check

*GATE: Passed against the framework and Elsa constitutions before implementation; re-check after design.*

- Layering: routing, binding, serialization, and authorization metadata remain in API modules; domain services remain unchanged.
- Framework honesty: mappings use ordinary ASP.NET Core endpoint builders and `RequestDelegate`s; no FastEndpoints replacement abstraction is introduced.
- Golden rule: immutable before observations and focused tests protect the public HTTP and OpenAPI contracts.
- Security: Foundation Identity owns permission policy semantics; handlers do not inspect claims to authorize requests.
- Collectibility: explicit mappers and static request delegates avoid process-global discovery and preserve owner lifecycle evidence.
- Subtractive change: the wave removes six feature-level FastEndpoints adapters, eight endpoint classes, and their unused references.
- Provisional material: framework constitution §2.24 is draft/provisional; ADR 0068 and the program issue provide the approved architecture direction.

## Project Structure

```text
src/Elsa/Api/Capabilities/
src/Elsa/Attention/Api/
src/Elsa/Expressions/Api/
src/Elsa/Expressions/JavaScript/Rendering/
src/Elsa/Workflows/Runtime/JavaScript/
src/Elsa/Workflows/Dashboard/
tests/Elsa/Architecture/
tests/Elsa/Api/Capabilities/Tests/
tests/Elsa/Attention/Api/Tests/
tests/Elsa/Expressions/Api/Tests/
tests/Elsa/Workflows/Dashboard/Tests/
tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Expressions/JavaScript/Rendering/Tests/
tests/Elsa/Workflows/Runtime/JavaScript/Tests/
specs/158-wave1-small-read-api-migration/
```

## Design Decisions

1. Each owner exposes one public static mapper and delegates to it from `IWebShellFeature.MapEndpoints`.
2. Handlers are static `RequestDelegate`s resolving module services from `HttpContext.RequestServices`; this keeps endpoint metadata stable and avoids a new endpoint framework.
3. Existing action permissions remain unchanged. Attention, JavaScript rendering, and Runtime JavaScript replace wildcard-only declarations with `attention.read`, `expressions.javascript.render`, and `workflows.runtime.javascript.execute`, each contributed by its owning module; wildcard remains accepted alongside each action permission.
4. Response metadata is declared with standard `ProducesResponseTypeMetadata` and `RequestDelegate.Invoke`; route-specific behavior remains in existing application services and local transport translation.
5. Each migrated owner owns a source-generated `JsonSerializerContext`. Request and response envelopes are explicit types, and endpoint execution uses generated `JsonTypeInfo` so collectible module types never enter the process-global reflection resolver cache.
6. FastEndpoints-before fixtures and mapping contract tests are immutable evidence; they are not generated or accepted automatically by the implementation.
7. Legacy operation IDs are preserved with `WithName`, and the host application tag is preserved with standard `ITagsMetadata` derived from `IHostEnvironment.ApplicationName`; no module-specific endpoint DSL is introduced. The runtime-JavaScript `RequestModel` schema identifier is retained by the explicit source-generated request contract.
8. The baseline's inaccurate JavaScript `204` and omitted-error-status declarations are recorded as explicit compatibility exceptions. New metadata advertises truthful runtime behavior and remains review-gated rather than silently claiming parity.

## Rollback

Revert the single Wave 1 commit to restore the six FastEndpoints feature adapters, eight endpoint classes, transition registry entries, and original project references. No persistence or public route migration is included.

## Risks

- Legacy FastEndpoints error serialization can differ from standard `Results`; focused HTTP fixtures must pin any required translation.
- Module-specific DTOs referenced by JSON/OpenAPI metadata can affect collectible-context retention; source-generated context ownership and repeated weak-reference evidence must remain green.
- New action permission names are public security vocabulary and must stay uniquely catalog-owned.
