# Test Quality Audit — Elsa 4 (elsa-foundation)

## Executive Summary

This is an unusually disciplined test suite for its size (~33 test projects, ~330+ test files found under the `*Tests.cs` convention, `dotnet test` on the flagship runtime project: **542/542 passing in 258ms**, no restore/build failures). The codebase's own constitution (`.specify/memory/constitution-framework.md` §2.23) explicitly separates **wiring tests** (§2.23.1, "proves wiring, not behaviour") from **implementation tests** (§2.23.2, "proves behaviour, not wiring") and mandates both. That self-awareness shows in the code: the best files in `tests/Elsa/Workflows/Runtime/Tests/**` and `tests/Elsa/Activities/**` genuinely pin observable behavior against real in-memory implementations (no mocking framework is used anywhere in the repo — 0 hits for Moq/NSubstitute), including a standout test (`WhileRealExpressionRuntimeTests.cs`) whose doc comment explicitly justifies avoiding a "counting mock" because it "cannot distinguish real observation from a hidden counter."

However, the registration-test mandate is executed with more brittleness than the constitution itself asks for. §2.23.1 requires only that registered services **resolve**; several suites (`WorkflowsRuntimeApiFeatureTests.cs`, `Architecture/FeatureRegistrationTests.cs`) go further and assert exact `ImplementationType` and `ServiceLifetime`, which pins internal wiring and will break on legitimate refactors (e.g., swapping one in-memory implementation for another with identical behavior). There's also real copy-paste duplication of hand-rolled fakes across test projects, and a runtime-engine test suite that is exceptionally strong at unit/contract level but has no dedicated "kill the process between commit and outbox" chaos test — recovery logic is tested only from pre-seeded state, not from an injected mid-flight crash.

---

## 1. Landscape Table

| Project | Kind | File Count | Verdict |
|---|---|---|---|
| `Elsa.Workflows.Runtime.Tests` | Unit + Contract (DTO invariants) + some E2E (feature-registration, drain-to-completion) | 51 | **Strong** — real in-memory engine impls, hand-rolled fakes, no mocks |
| `Elsa.Workflows.Design.Tests` | Unit + Sqlite-backed integration | 49 | Strong; real EFCore+Sqlite round-trips |
| `Elsa.Activities.Runtime.Tests` | End-to-end (via `WorkflowExecutionHarness`) | 34 | Strong — real DI container, real scheduler drain |
| `Elsa.Activities.ControlFlow.Tests` | End-to-end (loops/branches) | 28 | Strong — includes "real expression" tests, not counting mocks |
| `Elsa.Activities.Design.Tests` | Unit + Registration + Sqlite integration | 27 | Mixed — registration ritual present |
| `Elsa.Diagnostics.OpenTelemetry.Tests` | Unit + persistence | 14 | Adequate |
| `Elsa.Agent.Tests` | Unit | 13 | Adequate |
| `Elsa.Diagnostics.StructuredLogs.Tests` | Unit + persistence | 11 | Adequate |
| `Elsa.Secrets.Tests` | Unit + registration | 10 | Ritual risk |
| `Elsa.Activities.Flowchart.Tests` | End-to-end | 10 | Strong |
| `Elsa.Modularity.Tests` | Unit | 9 | Adequate |
| `Elsa.Workflows.Publishing.Api.Tests` | Unit | 8 | Adequate |
| `Elsa.Workflows.Design.Persistence.Groundwork.Tests` | Persistence/contract | 7 | Strong |
| `Elsa.Persistence.Groundwork.Tests` | Persistence/contract, dual-provider (sqlite+memory) | 7 | **Strong** — genuine round-trip contract tests |
| `Elsa.Foundation.Identity.Tests` | Unit | 7 | Adequate |
| `Elsa.Expressions.Tests` | Unit | 5 | Adequate |
| `Elsa.Architecture.Tests` | Architecture/fitness-function | 5 | **Strong** — real `.csproj`/`.slnx` scans, real invariants |
| `Elsa.Activities.Sequence.Tests` | End-to-end | 5 | Adequate |
| `Elsa.Activities.Design.Persistence.Groundwork.Tests` | Persistence | 5 | Adequate |
| `Elsa.Activities.Composition.Tests` | Unit | 5 | Adequate |
| `Elsa.Primitives.Hosting.Tests` | Unit/registration | 4 | Ritual risk |
| `Elsa.Serialization.Tests`, `Groundwork.Querying.Tests`, `StructuredLogs.Persistence.Tests`, `OpenTelemetry.Persistence.Tests`, `Caching.Tests` | Unit/persistence | 2 each | Thin but fine |
| `Elsa3.Mapping.Tests`, `Samples.Nuplane...Tests`, `Groundwork.UnifiedHost.Tests`, `Persistence.EFCore.Tests`, `Persistence.Core.Tests`, `ConsoleLogStreaming.Tests` | Unit | 1 each | Thin/smoke-level only |
| `Elsa.Activities.Testing`, `Design.Tests.ClrFixture` | Shared test infrastructure (no tests) | 0 | Support libraries (harness, fixtures) |

No `tests/**` project uses **Moq/NSubstitute/FakeItEasy** (0 references in any `.csproj` or `.cs`). Test doubles are 100% hand-rolled (`InMemory*Store`, `Recording*Handler`, `Fake*`).

---

## 2. Behavior vs. Implementation — Verdict with Strongest Examples

**Strongest behavior-pinning example:**
`tests/Elsa/Activities/ControlFlow/Tests/While/WhileRealExpressionRuntimeTests.cs:29-38` — doc comment: *"End-to-end termination coverage for `While` using a **real expression** (JavaScript via Jint), not a counting mock... the counting-mock runtime tests cannot distinguish real observation from a hidden counter, so this locks the supported path."* Real Jint JS engine, real durable-value persistence, real `While` activity, asserted via `run.AssertWorkflowCompleted()`. This survives refactors of internal loop bookkeeping and would catch a real regression in re-projection of mutated variables (the "#286" fix it documents).

`tests/Elsa/Workflows/Runtime/Tests/RuntimeSchedulerDrainTests.cs:105-133` — `DrainAsync_StopsBeforeDequeuingWorkOnceWorkflowReachesTerminalStatus` drives a real `InMemoryWorkflowSchedulerWorkQueue` + real `WorkflowSchedulerDrainer` against a real `InMemoryWorkflowExecutionStateStore`, referencing a specific regression (#293) in the comment. Behavior-level, regression-anchored.

`tests/Elsa/Architecture/ArchitectureGuardTests.cs:67-119` — scans real `.csproj` files for `Core_projects_do_not_reference_implementation_projects`, `Runtime_projects_do_not_add_design_references`. These are fitness functions on the actual solution, immune to mocking-related false confidence.

**Strongest implementation-pinning (ritual) example:**
`tests/Elsa/Workflows/Runtime/Tests/WorkflowsRuntimeApiFeatureTests.cs:16-270` — a single `[Fact]` (`RegistersRuntimeExecutionServicesAndRequestHandlers`) contains **47 separate `descriptor.ImplementationType == typeof(...)` assertions** for one feature's DI registration. This pins the exact concrete class chosen for every interface — renaming/consolidating an in-memory implementation (a pure refactor) breaks this test even though behavior is unchanged. Contrast with the constitution's own §2.23.1 text (`.specify/memory/constitution-framework.md:782-784`): *"Asserts that every service the feature is expected to register **resolves**. The test proves the wiring. It does not prove behaviour."* — resolution, not exact type identity, is what's mandated. The suite over-delivers brittleness beyond its own spec.

`tests/Elsa/Architecture/FeatureRegistrationTests.cs:41-53` similarly asserts `ImplementationType == typeof(Elsa3WorkflowDefinitionImporter)` and `Lifetime == ServiceLifetime.Scoped` — implementation detail, not observable behavior.

---

## Findings

**TS-1 (Medium) — Registration tests over-specify beyond the constitution's own mandate.**
`WorkflowsRuntimeApiFeatureTests.cs:16-270` (47 `ImplementationType` assertions in one method); `Architecture/FeatureRegistrationTests.cs:38-53`. The constitution (`constitution-framework.md:777-784`) only requires resolvability. Pinning `ImplementationType`/`Lifetime` turns a wiring-smoke-test into a refactor tripwire for internal implementation swaps. *Recommendation:* downgrade these assertions to `provider.GetRequiredService<TInterface>()` resolvability checks (as the constitution literally specifies), reserving `ImplementationType` assertions only for cases where the concrete type is itself part of the public contract (e.g., "must default to the in-memory store unless overridden" — which some tests already do correctly, see TS-2).

**TS-2 (Low, positive) — "Overridable default" registration tests are the right shape.**
`WorkflowsRuntimeApiFeatureTests.cs:276-382` (`RegistersRuntimeDomainRetryPolicyAsOverridableDefault` and siblings) pre-register a custom implementation, run `ConfigureServices`, and assert the custom one wins. This tests an actual behavioral contract (extension-point precedence / `TryAdd` semantics), not internal wiring. Recommend using this pattern as the template to replace TS-1-style tests where feasible.

**TS-3 (Low) — Duplicated hand-rolled fake across two test projects.**
`tests/Elsa/Persistence/Groundwork/Tests/InMemoryDocumentStore.cs` (190 lines) and `tests/Elsa/Persistence/Groundwork/Querying/Tests/InMemoryDocumentStore.cs` (186 lines) are near-identical — a copy-pasted test double rather than a shared fixture. *Recommendation:* extract to a shared test-support project referenced by both.

**TS-4 (Info/positive) — No mocking framework anywhere; hand-rolled fakes are real implementations, not stubs of stubs.**
Confirmed via grep across all `.csproj`/`.cs`: 0 references to Moq/NSubstitute/FakeItEasy. Tests compose real `InMemory*` implementations with small `Recording*` observer doubles (`RuntimeSchedulerDrainTests.cs:18-19`). This avoids the "testing the mock's return value" failure mode.

**TS-5 (Medium) — Recovery/crash tests validate scanner *logic* against pre-seeded state, not an actual crash injection.**
`RuntimeRecoveryScannerTests.cs` exercises `ScanAsync` against manually constructed `OperationalState` records representing "already crashed" conditions (expired lease, stale heartbeat, `InterruptedExecutionStatus.Detected`). Good coverage of the recovery *decision* logic, but no test starts a workflow, aborts mid-checkpoint-commit, and asserts the recovery scanner + drainer bring it to a consistent state end-to-end. Cross-stage crash scenario is untested.

**TS-6 (Low) — `RuntimeCheckpointCommitTests.cs` (1429 lines, ~47 test methods) is pure DTO/contract validation, not commit application.**
By design: it validates `RuntimeCheckpointCommit` constructor invariants; actual atomic application is verified in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeCheckpointWriterTests.cs:20-83` (idempotent replay on duplicate commit id). Reasonable split, but worth a doc pointer so readers don't assume `RuntimeCheckpointCommitTests` is the durability test.

**TS-7 (Low) — Minor flakiness-risk pattern: polling loop with `Task.Delay(10)`.**
`RuntimeInProcessAgentProviderTests.cs:205-216` (up to 100×10ms). Acceptable (polls + final hard assert), but the one wall-clock dependency in the sampled suite. Everywhere else `FixedTimeProvider`/injected `DateTimeOffset` is used consistently.

**TS-8 (Info) — Dual-provider persistence contract tests are a high-value pattern.**
`GroundworkRuntimeStateStoreTests.cs` runs every store contract test via `[InlineData("sqlite")]`/`[InlineData("memory")]` against real `SqliteConnection` (`:memory:`). Genuine cross-provider behavioral testing for the Groundwork document-store bridge layer. Design-time workflow persistence is similarly Sqlite-backed via real EFCore `DbContext`.

**TS-9 (Medium) — No standalone cancellation-contract test file in the runtime suite.**
No dedicated `RuntimeCancellationContractTests.cs`. Cancellation-adjacent assertions are scattered (`RuntimeSchedulerDrainTests.cs`, `RuntimeDownstreamSchedulingTests.cs`, `ExecuteWorkflowRequestHandlerTests.cs`), but no single place proves "cancel while suspended," "cancel with pending post-commit intents," or "cancel racing a resume" as first-class scenarios.

**TS-10 (Info) — Full suite duration/health.** `dotnet test` on runtime project → **542 passed, 0 failed, 258ms**. Fast, deterministic. Full-repo suite not run in this pass; recommend a CI whole-repo baseline.

---

## Gap List — Missing High-Value Test Scenarios (prioritized)

1. **Process-crash simulation between checkpoint commit and outbox delivery** — actually interrupt a running harness between `CommitAsync` and outbox drain, then run recovery scanner + drainer, asserting same terminal state as an uninterrupted run. (Closes TS-5.)
2. **Concurrent commit / lease-fencing test** — two workers racing to commit against the same execution with different fencing tokens; assert the stale one is rejected (fencing-token exists in `RuntimeExecutionLease.fencingToken`; no test asserts stale-fence rejection).
3. **First-class cancellation contract suite** (TS-9).
4. **Whole-repo `dotnet test` baseline in CI** with timing.
5. **De-duplicate `InMemoryDocumentStore` fakes** (TS-3).
6. **Downgrade `ImplementationType`-pinning registration assertions** (TS-1) to resolvability-only.

## Open Questions

- Is the 47-assertion registration test intentionally exhaustive (a deliberate "wiring manifest") or organic accretion? Worth an architect ruling since §2.23.1 doesn't require type-level pinning.
- Constitution §2.23.6 puts cross-feature integration testing "out of scope"/unratified — are the crash/recovery and lease-fencing gaps (items 1–2) *intentionally* deferred, or simply not yet written?
