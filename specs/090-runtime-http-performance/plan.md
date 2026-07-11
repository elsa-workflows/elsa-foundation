# Implementation Plan: Runtime HTTP Hot-Path Performance

**Branch**: `597-runtime-http-performance` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/090-runtime-http-performance/spec.md`

## Summary

Productionize the existing crash-safe checkpoint-coalescing policy as a provider-neutral shell feature applied after all runtime stores are configured, enable it in the reference server, and prove the synchronous `HttpEndpoint → WriteHttpResponse` path over Groundwork SQLite reduces physical checkpoint commits while preserving the authored response, terminal state, inspection evidence, outbox semantics, fencing, and crash recovery. Add deterministic integration coverage for write amplification and a separate opt-in latency harness for cold/warm percentile evidence.

## Technical Context

**Language/Version**: C# / .NET 10; Bash for the opt-in local measurement command

**Primary Dependencies**: CShells feature composition, ASP.NET Core TestServer, Elsa Workflows Runtime, Elsa Activities HTTP, Groundwork Documents + Groundwork SQLite

**Storage**: Groundwork SQLite for reference durable evidence; existing in-memory runtime stores remain the control lane

**Testing**: xUnit; existing Runtime, Groundwork, Activities HTTP integration, and Architecture test projects

**Target Platform**: Single-node ASP.NET Core server on macOS/Linux/Windows; provider-neutral runtime composition

**Project Type**: Modular .NET web-service foundation with feature-composed runtime providers

**Performance Goals**: Warm synchronous hello-world p95 ≤50 ms on the reference local durable provider; ≥75% reduction from the observed thirteen physical checkpoint commits

**Constraints**: Mandatory durability boundaries, durable-queue frontier, post-commit atomicity, ownership fencing, response semantics, and crash convergence cannot regress; ordinary CI must not depend on wall-clock budgets

**Scale/Scope**: One new runtime shell feature and settings contract, one real SQLite-backed HTTP integration lane, one deterministic cap-overflow regression, one opt-in measurement script, reference-server configuration and documentation

## Constitution Check

*GATE: Passed before research and re-checked after design. The constitution remains draft; this plan treats its runtime and persistence rules as binding quality gates.*

| Gate | Status | Evidence |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | The policy and decorators remain in the existing Runtime implementation. The new shell activation stays in the existing Runtime API composition project; no new `.Core` or provider project is introduced. |
| Framework §2.6.2 replacement contracts | PASS | `IRuntimeCheckpointPersistencePolicy` remains a single selected implementation. Coalescing decorates the selected store implementations only after provider composition. |
| Framework §2.11 / §2.19 feature dependency and identity | PASS | A new stable shell feature identity depends on `WorkflowsRuntimeApi`; the post-configuration hook handles mutually exclusive providers without abusing JSON ordering as a dependency mechanism. |
| Framework §2.12 / Elsa §E4 configuration status | PASS WITH DRAFT NOTE | Configuration classification is deferred, so the settings are explicitly host/shell-scoped and do not claim tenant/workflow scope. |
| Framework §2.20 provider decomposition | PASS | The policy feature is provider-neutral. SQLite-specific evidence lives in tests/tooling and does not move SQLite logic into Runtime. |
| Framework §2.21 / §2.23 test discipline | PASS | New behavior is introduced test-first; feature registration, provider capture, real HTTP integration, cap overflow, crash recovery, and architecture composition are covered. Existing tests are preserved. |
| Elsa §E2.2 Design/Runtime split | PASS | Runtime configuration and execution changes introduce no Runtime → Design dependency. |
| Elsa §E2.4 foundation composition | PASS | The reference server opts into a default implementation appropriate for local development while provider selection stays host-owned. |
| Elsa §E6 naming | PASS | Proposed names (`CheckpointPersistenceMode`, `WorkflowsRuntimeCheckpointPersistenceFeature`) stay within the component budget and use protected domain nouns. |
| Durable resumption invariant | PASS | Coalescing uses the shipped queue-frontier, mandatory-boundary, fencing, and two-generation convergence implementation unchanged. |

## Phase 0 Research Decisions

Research conclusions are recorded in [research.md](./research.md). The decisive findings are:

- Apply coalescing from `IPostConfigureShellServices`, because CShells invokes it only after every enabled feature has configured or replaced runtime stores.
- Keep Immediate as the platform compatibility default; enable Coalesced explicitly in the reference server configuration.
- Count persisted `checkpointCommit` documents in isolated SQLite databases for deterministic physical-write assertions.
- Keep wall-clock latency in an opt-in measurement command; CI gates response correctness, commit structure, cap behavior, and recovery.
- Do not add SQLite durability profiles or new dispatch semantics unless the measured coalesced path still misses the budget.

## Phase 1 Design

### Composition contract

Add `WorkflowsRuntimeCheckpointPersistenceFeature` in the Runtime API composition project. It implements `IShellFeature` and `IPostConfigureShellServices`:

1. `ConfigureServices` intentionally performs no store decoration.
2. `PostConfigureServices` validates the selected mode and cap against the fully populated service collection.
3. Immediate mode leaves the selected provider registrations untouched.
4. Coalesced mode invokes the existing `AddCoalescingRuntimeCheckpointPersistence` extension with the configured cap.

This is a leaf shell feature, not a new project. It depends on `WorkflowsRuntimeApi`, giving the post-configurer at least the default in-memory runtime contracts while allowing SQLite, PostgreSQL, or unified providers to replace them during normal configuration.

### Deterministic evidence

Extend the existing Activities HTTP integration fixture with runtime persistence options. Each durable-policy fixture owns a temporary SQLite database and queries the `checkpointCommit` document kind before and after the request. The comparison asserts:

- identical HTTP status, headers, and body;
- identical completed workflow state and durable response instruction;
- Immediate physical commits exceed Coalesced commits;
- Coalesced reduces physical commits by at least 75% and reaches the exact measured mandatory minimum;
- isolated databases prevent cross-test count ambiguity.

Add a runtime-level cap test that drives a segment longer than caps `1`, `5`, and `50`, records inner physical commits, and proves the buffer never crosses the cap. Existing mandatory-boundary and two-generation Groundwork crash tests remain the recovery authority.

### Performance evidence

Add an opt-in Bash command that measures an already-published endpoint. It records environment metadata, separates cold/warm samples, calculates p50/p95/p99/max, validates the expected response, and optionally reads a Groundwork SQLite database snapshot to report physical checkpoint-marker growth. Budget enforcement is opt-in so ordinary developer/CI runs remain deterministic.

### Reference server

Enable `WorkflowsRuntimeCheckpointPersistence` with `Mode = Coalesced` and `MaxSegmentCheckpoints = 50` in both server shell configuration snapshots. Immediate rollback is a one-value configuration edit documented by the quickstart and feature setting description.

## Project Structure

### Documentation (this feature)

```text
specs/090-runtime-http-performance/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-http-performance.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Api/Coalescing/
├── CheckpointPersistenceMode.cs                         # new
├── WorkflowsRuntimeCheckpointPersistenceFeature.cs     # new
└── CoalescingRuntimeCheckpointPersistenceExtensions.cs # existing reuse

src/Apps/Elsa.Server/
├── shells.json
└── shells.baseline.json

tests/Elsa/Workflows/Runtime/Tests/
├── RuntimeCheckpointCoalescingTests.cs
└── WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs # new

tests/Elsa/Activities/Http/IntegrationTests/
├── Elsa.Activities.Http.IntegrationTests.csproj
├── HttpEndpointHostFixture.cs
└── HttpEndpointRuntimePerformanceTests.cs               # new

tools/performance/
└── measure-http-workflow.sh                             # new
```

**Structure Decision**: Reuse the existing Runtime API composition project because this is a host-selectable activation of existing runtime policy, not a new provider or domain. Reuse the real HTTP integration fixture and add only the provider references needed for durable evidence. Keep measurement tooling outside ordinary tests.

## Post-Design Constitution Re-check

All pre-research gates remain passing. The design adds no project, persistence model, public runtime `.Core` contract, Runtime → Design reference, wire-format change, or mandatory-boundary configurability. The post-configuration hook is existing CShells lifecycle infrastructure and is the narrowest mechanism that can safely decorate whichever mutually exclusive provider the host selected.

## Complexity Tracking

No constitutional violations or exceptions are required.
