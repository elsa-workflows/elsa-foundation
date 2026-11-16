# Implementation Plan: Weaver session, approval and outcome recovery

**Branch**: `agent/claude/3441-weaver-recovery` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Status**: Approved for task generation after an independent planning review on 2026-11-02. Its verified findings are incorporated:
- lease extension on write, and a retryable owned-elsewhere outcome;
- claims serialized with policy writes on the session write gate;
- single-flight hydrate with compare-and-set settlement;
- proposal-to-session resolution;
- durable audit as a trio;
- the complete persisted models;
- compare-and-set for withdraw;
- authorization before hydration;
- one definition of the authorization pass;
- Pause-claim busy semantics.

No shared contract is frozen.

**Implementation base**: main `8da103a38`, which contains #3440 (`95e4e9610`) and its documentation follow-up #3638.

## Summary

Give the existing session owner durable backing without replacing it. The default
`InMemoryAgentSessionService` stays the one owner, so session-policy participation keeps working. It
writes through one optional replacement contract, `IAgentSessionRecordStore`, for its session
aggregate. An opt-in EF Core feature implements that contract on the four ADR 0073 engines. Claims
stay in memory and are rebuilt from durable facts when a session is hydrated. Settlement is lazy and
records only zero-effect outcomes without a present user. A fence and lease stop a superseded
process from writing. Disclosure snapshots are stored once by identity, and fingerprints stay
byte-identical.

Studio reopens sessions from a new owner-scoped list. It shows recovery outcomes and offers explicit
start, withdraw, execute, decide and resolve actions. It never reattaches to a turn automatically.
Provider behaviour after an interruption becomes explicit: the Copilot reset is surfaced, and
Anthropic history excludes non-accepted turns. Decisions and evidence are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10; TypeScript and React; Python 3 for the conformance driver.

**Primary Dependencies**:
- Agent Core, Agent and Agent API;
- the actual GitHub Copilot and Anthropic adapters;
- the shared EF persistence family (`Essentials/src/Persistence/EntityFramework`) and its module tooling;
- Secrets (`ISecretValueProtector`);
- the Studio public SDK.

**Storage**:
- In-memory by default, as today.
- Opt-in EF Core store with SQLite, SQL Server, PostgreSQL and MySQL contexts and per-provider migrations.

**Testing**:
- xUnit behaviour tests with controlled barriers and the existing fake clock, including restart simulation (dispose the host, then rehydrate from the same store).
- EF provider-contract tests: SQLite in the PR tier, Testcontainers for the other three engines in the EF container tier.
- Vitest for the Studio client.
- Playwright and native composed journeys.
- The package conformance witness extended with restart cases on both actual adapters.

**Target Platform**: The supported .NET backend and the Studio browser; local macOS and hosted Linux package consumers. No deployment, publishing or live-provider credentials.

**Project Type**: A modular .NET framework extension (Agent) plus a React host client.

**Performance Goals**:
- No monitor is held across store, provider or module I/O.
- Exactly one awaited write per lifecycle transition.
- Owner list and audit queries are bounded by take and cursor.
- Snapshot storage grows with the number of distinct snapshots.

**Constraints**:
- Never replay an accepted effect.
- Disclosure and execution happen only under a present owner's current authority.
- Turns stay coupled to their stream.
- No detached turn owner and no second execution engine.
- The #3440 refusal order, claims and outcome rules are preserved.

**Scale/Scope**: The five #3441 acceptance criteria and three #3514 follow-through items. Workflow-runtime recovery stays with #2892 (#2912) and #3519. Broad workflow and current-module journeys stay with #3442 and #3443.

## Constitution Check

Both ratified constitutions are loaded (framework and Elsa), together with ADRs 0022, 0051, 0073 and 0080. The initial check passes with the constraints below. It is repeated after the contract is selected.

| Gate | Application |
| --- | --- |
| Framework §2.1/2.5/2.18 | Public contracts and models stay in Agent Core. Mechanisms stay in Agent. EF lives in its own persistence project. Generic Weaver does not reference modules. |
| §2.5.1 | The singleton owner holds no request or module callbacks. Scoped execution resolves current tools and authority. Recovery never captures an `HttpContext`. |
| §2.6.2/2.11 | `IAgentSessionRecordStore` is a declared replacement contract, and a second registration fails with a diagnostic (R6). An unsupported composition refuses explicitly and creates no shadow state. |
| §2.10/2.23.2 | Behaviour tests cover restart boundaries, fences, settlement, deduplication and authority. No test asserts documentation text. |
| §2.21/2.23.3/2.23.5 | Existing tests keep passing, adapted only where the contract becomes async. Implementation is internal by default. Infrastructure failures are wrapped at the domain boundary and map to FR-018 outcomes. |
| §2.22 | The Agent extension-point catalog, the root catalog and the EF module documentation are updated with the delivered surface. |
| §4, ADR 0080 D4 | Two changes are recorded pre-GA in the contract's SemVer section: the async `IAgentTurnStateStore` (Major) and the additive list, withdraw and resolution surface. |
| Elsa §E2.5 | The EF context is an opt-in capability of its own module. Core gains no EF dependency. |
| Elsa §E5/E7 | Package versions are computed. The new test projects are registered in `tools/ci/test-tiers.json` and the EF suite selector. Maps and solution filters are regenerated. |
| ADR 0073 | EF Core only, on the four named engines, with per-provider migrations and provider-contract proof. In-memory satisfies no durable gate. |
| ADR 0022 | Session identity is preserved, and the Copilot reset becomes explicit. No generation suffix without an amendment. |

No constitutional exception is planned.

## Project Structure

### Documentation (this feature)

```text
specs/1189-weaver-recovery/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/recovery.md
```

### Source Code (repository root)

```text
Extensions/Agent/src/
  Core/Contracts/AgentContracts.cs, AgentTurnContracts.cs        # record-store port, async turn state, list query
  Core/Models/AgentModels.cs
  Elsa.Agent/PublicAPI.Unshipped.txt
  Elsa.Agent/Services/DefaultAgentServices.cs                     # owner write-through, hydrate, fence, rebuild
  Elsa.Agent/Services/AgentTurnServices.cs                        # async pause, admission pass, interruption
  Elsa.Agent/Services/AgentProposalServices.cs                    # durable transitions, revision recheck
  Elsa.Agent/Services/AgentToolInvoker.cs                         # single insert for call keys
  Elsa.Agent/Services/AgentDisclosureHistory.cs                   # identity store, limits
  Elsa.Agent/Services/AgentRegistrationValidator.cs               # at most one record store
  Elsa.Agent/EXTENSION_POINTS.md
  Api/AgentApi.cs, Api/Models/AgentApiModels.cs                   # list, proposals, withdraw, resolution
  GitHubCopilot/Services/GitHubCopilotAgentProvider.cs            # explicit conversation reset
  Persistence/EntityFrameworkCore/                                # new: EF feature, contexts, migrations
Extensions/Agent/tests/
  Tests/                                                          # owner, restart, settlement, API
  Persistence/Tests/, Persistence/ProviderTests/                  # new: SQLite / Testcontainers
Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/
  agent/agentClient.ts, agent/agentTypes.ts
  weaver/useWeaverSession.ts, weaver/WeaverSurface.tsx, weaver/__tests__/
tools/studio/Weaver.Conformance.Probe/                            # restart journeys
tools/ef/, tools/ci/test-tiers.json, Elsa.Server.slnx             # EF tooling, tiers, solution
```

**Structure Decision**: Extend the existing Agent owner, API and Studio surface, and add exactly one new production project, the EF persistence feature. It follows the Specialists EF precedent. Before any edit, the exact paths are checked against main.

## Ordered Delivery

1. **Contract and owner seams, still in memory.**
   - Introduce `IAgentSessionRecordStore` with a default in-memory implementation that matches today's behaviour.
   - Make `IAgentTurnStateStore` async.
   - Route every owner transition through hydrate and an awaited write, then the monitor.
   - Fix the three-step call-key insert and the unloaded-row hazard.
   - Serialize claim acquisition and policy writes on the session write gate. Make hydration single-flight, with compare-and-set settlement. Resolve proposals to their session. Make message status changes compare-and-set.
   - Behaviour is unchanged, so all 737 existing Agent tests must still pass.
2. **Restart semantics.**
   - Rebuild claims at hydrate.
   - Lazy settlement and recovery attribution.
   - The interrupted-turn outcome.
   - The story 2.3 recheck on execute.
   - Paused-turn continuation after a restart.
   - Fence and lease, with `ownership_lost`.
   - Restart-simulation tests at all six boundaries, before and after deadlines.
3. **Disclosure identity.**
   - The snapshot store by digest, with fingerprints kept verbatim.
   - The pass-local admission set.
   - Both limits as distinct outcomes.
   - Causal tests: quadratic growth fails before the change and passes after it; fingerprint and pause stability across a restart.
4. **EF feature.**
   - Project, contexts, migrations for the four engines, codec with Secrets.
   - Durable audit: sink, reader and ownership replaced together.
   - PublicAPI files, `nuplane.json`, a README, `EXTENSION_POINTS.md` and an `EfModule` AssemblyInfo.
   - Registration and duplicate detection.
   - Provider-contract tests and the tier, tooling, solution and maps updates.
5. **API and Studio.**
   - API: the session list, proposals, withdraw and resolution routes, plus the execute recheck.
   - Studio: the remembered session hint, the reopen flow, recovery outcome rendering, explicit Start, withdraw, resolve and limit continuation.
   - The module adapter reports target availability.
   - Vitest and Playwright coverage.
6. **Providers.**
   - Copilot emits an explicit conversation-reset outcome.
   - Anthropic history excludes non-accepted turns.
   - Each provider's documented behaviour after an interruption.
7. **Qualification.**
   - Composed Workbench with the EF store configured on SQLite.
   - Native package witness: the existing 68 cases, plus restart cases on both actual adapters, against a durable store.
   - Affected complete suites, the architecture guard and maps.
   - Contract freeze only after the composed and package proofs.
   - Then independent review, exact-head checks, merge and resulting-main verification.

Every experiment is evidence bound to its source and inputs. Partial runs and runs expected to fail do not mark the feature qualified.

## Complexity Tracking

One new production project, which ADR 0073 requires for durable stores, and one new replacement contract. A second owner, per-store ports and a detached turn runner were rejected (research R1 and R4).
