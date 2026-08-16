# Implementation Plan: Wave 4 Agent REST and SSE API Migration

**Branch**: `codex/1370-wave4-agent-api` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Replace the eleven Agent FastEndpoints registrations with an explicit `MapAgentApi` Minimal API
seam. Freeze and compare the real FastEndpoints-before HTTP/OpenAPI/SSE observations, preserve
operation names and wire behavior, contribute Agent-owned permissions, and prove coexistence,
authorization, streaming cleanup, and collectible owner lifecycle.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, CShells `IWebShellFeature`, Foundation Identity
authorization/catalog, Elsa Agent services, FastEndpoints coexistence canary, xUnit/TestServer,
`Elsa.Api.Compatibility.Testing`

**Storage**: Existing Agent services and stores; no schema changes

**Testing**: Immutable before fixtures, HTTP/OpenAPI comparer, authorization matrix, SSE lifecycle,
collectible `AssemblyLoadContext`, transition ratchet, maps, architecture suite, and full build

**Constraints**: Exactly eleven registrations; no public route redesign; no blanket/write-delta
  approvals; no new endpoint DSL; no broad module migration; preserve baseline SSE framing

## Constitution Check

*GATE: Passed against the framework and Elsa constitutions before implementation; §2.24 remains
draft/provisional and is noted rather than treated as ratified architecture.*

- Layering: endpoint mapping, transport translation, and security metadata remain in the Agent API;
  domain and service contracts remain unchanged.
- Framework honesty: ordinary ASP.NET Core endpoint builders and `RequestDelegate`s are used; no
  Elsa-owned replacement for FastEndpoints is introduced.
- Golden rule: immutable before fixtures and exact comparer approvals protect public HTTP/OpenAPI
  and SSE behavior.
- Security: Foundation Identity owns policy semantics and wildcard evaluation; handlers do not parse
  claims or hide permissions in path middleware.
- Collectibility: owner-local generated JSON contexts and explicit route publication are exercised
  by repeated unload tests.
- Subtractive change: the eleven Agent endpoint adapters and production FastEndpoints dependency are
  removed while transitional support remains for unrelated owners.

## Design Decisions

1. Map all eleven routes explicitly and retain legacy `WithName` operation identifiers, route
   templates, response metadata, and Agent tags.
2. Use static request delegates resolving existing Agent services from `HttpContext.RequestServices`.
   This keeps module ownership explicit and avoids process-global endpoint discovery.
3. Contribute `agent.use`, `agent.proposals`, and `agent.audit` from `Elsa.Agent.Api`; record the
   proposal-to-use implication in the contributor and test it through the shared evaluator.
4. Use standard `RequireAuthorization` policy metadata and a separate SSE source-generated context
   with default serializer options where the legacy event casing/numeric enum contract requires it.
5. Use the compatibility comparer with an empty approval set. Any future intentional difference
   must be an exact reviewed route-scoped approval, never a blanket or write-delta escape.
6. Keep heartbeat/resume/backpressure additions out of this migration because the frozen contract
   contains framing, cancellation, and cleanup but no heartbeat or resume protocol.

## Project Structure

```text
src/Elsa/Agent/Api/AgentApi.cs
src/Elsa/Agent/Api/AgentJsonContext.cs
src/Elsa/Agent/Api/Authorization/AgentPermissionContributor.cs
src/Elsa/Agent/Api/FoundationAgentApiFeature.cs
tests/Elsa/Agent/Tests/AgentApiMappingTests.cs
tests/Elsa/Architecture/Wave4Agent*Tests.cs
tests/Elsa/Architecture/Baselines/wave4-agent-*-fastendpoints.json
docs/reports/wave-4-agent-api-migration-2026-08.md
```

## Rollback and Risks

The implementation is split after commit `9293cb029`, which freezes the FastEndpoints-before
evidence. Reverting the migration commit restores the eleven endpoint adapters and transition
entries without changing persistence. Risks are limited to serializer metadata/casing, SSE
cancellation/disposal, and authentication delegate retention; each has a focused gate.
