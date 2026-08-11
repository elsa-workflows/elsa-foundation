# EF Core oracle scoping — Step 1 findings

Status: decision input for [Zero-EF Persistence](../program-goals/zero-ef-persistence.md).

Date: 2026-08-11.

Scope: verification only. No EF Core code, Groundwork adapter, or other production code was modified.

Tracking: [#646](https://github.com/elsa-workflows/elsa-foundation/issues/646) (performance verdicts),
[#647](https://github.com/elsa-workflows/elsa-foundation/issues/647) (final removal lane),
[#629](https://github.com/elsa-workflows/elsa-foundation/issues/629) (zero-EF PRD).
Spec: [144-zero-ef-final-removal](../../specs/144-zero-ef-final-removal/spec.md).

## Why this report exists

A strategy report merged into the Groundwork repository established one hard ordering constraint: EF
Core is Elsa's only oracle, the evidence authorising its removal needs it as an input, so harvesting
the EF ↔ Groundwork comparison must precede deleting EF. Every other step is recoverable; destroying
the oracle is not.

That constraint is sound. This report verifies the six assumptions the downstream costing rests on
and finds that **three of them do not hold as stated**, in a direction that materially changes the
plan. The headline is not that the work is bigger. It is that the oracle covers far less of the tree
than assumed, and that most of the work assumed to be pending is already built.

## Summary of verdicts

| # | Assumption | Verdict |
|---|---|---|
| 1 | Every runtime persistence seam has both an EF-backed and a Groundwork-backed implementation, selectable by DI/config | **False.** EF implements **zero** runtime persistence seams. Only 3 lanes are dual-stack. |
| 2 | Both are exercised by a common test suite, or could be without per-implementation forks | **Holds, and stronger than assumed.** The shared differential already exists for all 3 dual-stack lanes. |
| 3 | "Ten runtime persistence seams plus a durable checkpoint writer" | **Structure right, count wrong.** ~25 seam stores + the checkpoint writer; 36 contract registrations; 27 document kinds. |
| 4 | Locks/leases live in `src/Elsa/Locking` and may be neither stack | **Confirmed neither stack.** Locks are a third-party file-system library; leases are Groundwork-side. Zero EF exposure. |
| 5 | A benchmark project exists, or a latency harness must be built from zero | **Exists, and is substantial.** Gates, statistics, 13 frozen workloads, provenance, child-host protocol. **No EF adapter, at all.** |
| 6 | A realistic start/resume/checkpoint workload exists to drive | **Exists at two layers.** What is missing is an EF comparand, not a workload. |

The single most consequential finding is not in that table. It is this: **for 11 of the 13 frozen
workloads, the declared "EF contract baseline" is a hand-written description of expected observable
behaviour, not runnable EF code — and it can never become runnable, because no EF implementation of
those seams exists or ever existed.** See [The oracle is much smaller than the
plan assumes](#the-oracle-is-much-smaller-than-the-plan-assumes).

---

## 1. Dual-implementation seams — **assumption false**

EF Core exists in exactly five source locations, and **none of them implements a runtime
persistence seam**:

| EF source root | What it is |
|---|---|
| `src/Elsa/Persistence/EFCore/` | Generic shell base: `EFCorePersistenceShellFeatureBase<TDbContext>`, aggregating save/load handlers, bulk upsert, migrations startup task. Implements no seam itself. |
| `src/Elsa/Diagnostics/OpenTelemetry/Persistence/EFCore/` | `EfCoreOpenTelemetryStore` — a real `IOpenTelemetryStore` implementation. |
| `src/Elsa/Diagnostics/StructuredLogs/Persistence/EFCore/` | `EfCoreStructuredLogStore` — a real `IStructuredLogStore` implementation. |
| `src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/` | `ApplicationIdentityDbContext` + framework identity stores + `TenantMembershipEntity`. |
| `src/Elsa/Foundation/Identity/OpenIddict/EntityFrameworkCore/` | OpenIddict's own EF store. **No Groundwork counterpart exists** — this lane is EF-only. |

`src/Elsa/Persistence/EFCore/` contains no implementation of any runtime store contract. A search
for the runtime seam interfaces across that tree returns nothing:

```bash
grep -rn "IBookmarkStateStore\|IWorkflowExecutionStateStore\|IActivityExecutionStateStore\|\
IRuntimeCheckpointCommitStore\|IWorkflowTriggerBindingStore\|IDurableTimerStore\|\
IWorkflowDispatchStore\|IRuntimePostCommitOutboxStore\|ISchedulerStateStore\|\
IWorkflowSchedulerWorkQueue" --include=*.cs src/Elsa/Persistence/EFCore/
# no matches
```

**This finding is not novel, and that strengthens it.** `runtime-absolute-budget-basis.md`, **ratified
2026-08-04**, states the same fact for the 21 runtime coverage-ledger rows — *"None of them can ever
receive an EF-ratio verdict, because no runtime persistence seam has ever had an EF-Core-backed
implementation"* — and supplies a stronger historical proof than the present-tense grep above:

```bash
git log --all -- "src/Elsa/Workflows/Runtime/**EFCore**"   # empty
```

So EF's absence from the runtime seams is not merely current state that a future commit might change;
it is the ratified basis on which the runtime rows were moved to absolute budgets. What is new here is
the consequence for the *oracle-harvesting* obligation, which that document does not address.

### What the runtime seams' second implementation actually is

It is **in-memory**, not EF. `AddGroundworkRuntimeStores` describes its own job as replacing them:

> `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs:59` —
> *"Replace the in-memory defaults registered by the runtime API feature."*

The in-memory family is at `src/Elsa/Workflows/Runtime/Services/InMemory*.cs` — 30 files, registered
by `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs:198-203`.

This matters for oracle design. An in-memory store is **not** a durability oracle: it has no
transaction boundary to roll back, no restart to survive, no provider-side OCC, and no
materialization semantics. It can differentially validate ordering, null handling, and idempotency
logic; it cannot validate the four properties the latency-and-durability gate exists to protect.
Treating in-memory as the comparand where EF is absent would produce a green differential that
proves much less than it appears to.

### The three genuinely dual-stack lanes

| Lane | EF implementation | Groundwork implementation | Selectable by |
|---|---|---|---|
| Diagnostics — OpenTelemetry | `…/OpenTelemetry/Persistence/EFCore/Storage/EfCoreOpenTelemetryStore.cs` | `…/OpenTelemetry/Persistence/Groundwork/GroundworkOpenTelemetryStore.cs` | Shell feature registration |
| Diagnostics — Structured Logs | `…/StructuredLogs/Persistence/EFCore/Storage/EfCoreStructuredLogStore.cs` | `…/StructuredLogs/Persistence/Groundwork/GroundworkStructuredLogStore.cs` | Shell feature registration |
| Identity — ASP.NET Core Identity | `…/AspNetCoreIdentity/EntityFrameworkCore/` | `…/AspNetCoreIdentity/Groundwork/Stores/` | Feature registration |

### Provider scope is narrower than "SQLite, PostgreSQL, SQL Server"

The brief scopes the oracle to SQLite, PostgreSQL and SQL Server. In the shipped tree **EF has
SQLite only.** The only EF provider projects are `Elsa.Persistence.EFCore.Sqlite`,
`…OpenTelemetry.Persistence.EFCore.Sqlite`, and `…StructuredLogs.Persistence.EFCore.Sqlite`. There
is no EF PostgreSQL or SQL Server wiring anywhere in `src/`.

PostgreSQL and SQL Server EF packages appear in exactly one place — the test project
`tests/Elsa/Persistence/EFCore/Tests`, where they back `UpsertCommandGeneratorPostgresTests` and
`UpsertCommandGeneratorSqlServerTests`. Those are **SQL-string generation unit tests**, not a
running provider.

The differential harness already records this constraint in source, and states the consequence
precisely:

> `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/Differential/OpenTelemetryDifferentialTarget.cs:26`
> — *"SQLite on both sides is forced, not chosen: EF Core has no PostgreSQL or SQL Server wiring
> anywhere in `src/`. Groundwork's other three providers are covered by its own conformance matrix,
> which is strictly less evidence than a differential and must not be reported as equivalent
> assurance."*

**The oracle is SQLite-only.** MongoDB having no oracle is structural, as the brief says — but so is
PostgreSQL and SQL Server. Any plan that promises a three-provider differential is promising
something the tree cannot produce without first *writing* EF providers, which directly contradicts
the removal goal.

---

## 2. Common test suite — **holds, and the work is already done**

The assumption was that a common suite exists or could be built without per-implementation forks.
The reality is better: **a shared, parameterized behavioural differential already exists for all
three dual-stack lanes**, and it covers exactly the dimensions the brief lists as the behavioural
differential deliverable.

| Lane | Differential location |
|---|---|
| OpenTelemetry | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Tests/Differential/` (4 files, 778 lines) |
| Structured Logs | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Tests/Differential/` (4 files, 712 lines) |
| ASP.NET Core Identity | `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Differential/` |

Each is a `[Theory]` over an identical six-dimension enum, with both stacks constructed as
comparands in the same test method:

| Dimension | Brief's requested assertion |
|---|---|
| `ConcurrencyConflictShape` | OCC conflict detection *and the shape of the failure* |
| `ProducerOrdering` | Ordering under concurrent producers |
| `NullAndDefaultMaterialization` | Missing/null handling |
| `RollbackVisibility` | Transaction and unit-of-work rollback boundaries |
| `RestartObservation` | Restart observations |
| `IdempotentReplay` | Idempotency and retry on replay |

That is a one-to-one match with the brief's behavioural-differential list. Entry points:

- `…/OpenTelemetry/…/Differential/OpenTelemetryStoreDifferentialTests.cs:32`
- `…/StructuredLogs/…/Differential/StructuredLogStoreDifferentialTests.cs:36`
- `…/Identity/Tests/AspNetCoreIdentity/Differential/TenantMembershipStoreDifferentialTests.cs:35`

all named `Ef_and_groundwork_agree_or_carry_a_recorded_disposition`, each backed by a divergence
ledger (`OpenTelemetryDivergenceLedger`, `StructuredLogDivergenceLedger`) so an intentional
difference is recorded rather than silently passing.

The fixture shape the brief predicts — "one interface, two registrations, identical workload" — is
what these already are, via an abstract `…DifferentialTarget` with `EfCore()` and `Groundwork()`
factories over a shared on-disk SQLite database that deliberately survives `CloseAsync` so restart
probes observe real durability.

The shared test projects reference **both** stacks, confirming DI-level selectability:
`Elsa.Diagnostics.OpenTelemetry.Persistence.Tests.csproj` and
`Elsa.Diagnostics.StructuredLogs.Persistence.Tests.csproj` each carry both the EFCore(+Sqlite) and
Groundwork adapter project references plus `Groundwork.Sqlite`.

One operational caveat worth preserving: both csproj files carry a comment explaining that they
deliberately avoid the shared Groundwork provider-driver test library and avoid naming the
container-based provider package family, because `ci.yml` selects the fast PR gate **by grepping
csproj text**. A future refactor that "tidies" these references into the shared library will silently
demote the differential to nightly-only CI.

---

## 3. Seam inventory — structure right, count wrong

Groundwork ADR 0004 cites "ten runtime persistence seams plus a durable checkpoint writer". The
**structure** is exactly right, and the checkpoint writer is real and distinct:

> `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs:177-179`
> — *"Durable checkpoint writer. It orchestrates the Groundwork-backed seam stores above and records
> a restart-safe per-CommitId marker, replacing the in-memory writer registered by the runtime
> feature."* → `lane.Replace<IRuntimeCheckpointCommitStore, GroundworkRuntimeCheckpointWriter>()`

The **count of ten is stale by roughly 2.5×**. Three independent counts against
`AddGroundworkRuntimeStores` and `ElsaRuntimeStorageManifest`:

| Measure | Count |
|---|---|
| Distinct seam contracts registered (`lane.Replace` / `lane.Alias` interfaces) | **36** |
| Distinct Groundwork seam store implementations (excluding infrastructure) | **~25**, plus `GroundworkRuntimeCheckpointWriter` |
| Document kinds declared in `ElsaRuntimeStorageManifest` | **27** |

The 36-vs-25 gap is aliasing, not disagreement: several contracts are read/write facets of one
store (e.g. `IWorkflowDispatchStore` and its Query/Delete/RetentionRoot/Admission/Cancellation
aliases all resolve to `GroundworkWorkflowDispatchStore`; the five post-commit outbox contracts all
resolve to `GroundworkRuntimePostCommitOutboxStore`).

The runtime seam stores, from `src/Elsa/Persistence/Groundwork/Stores/`:

`GroundworkActivityExecutionHierarchyStore`, `GroundworkActivityExecutionInspectionStore`,
`GroundworkActivityExecutionStateStore`, `GroundworkBookmarkStateStore`,
`GroundworkDurableTimerStore`, `GroundworkDurableValueStateStore`,
`GroundworkExecutableActivityTemplateStore`, `GroundworkExecutionLivenessStateStore`,
`GroundworkIncidentStateStore`, `GroundworkRecurringTriggerScheduleStore`,
`GroundworkRuntimePostCommitOutboxStore`, `GroundworkRuntimeRecoveryScanner`,
`GroundworkSchedulerStateStore`, `GroundworkTestScopeCleanupStore`,
`GroundworkWorkflowAlterationStore`, `GroundworkWorkflowDispatchStore`,
`GroundworkWorkflowExecutableSourceReferenceStore`, `GroundworkWorkflowExecutableStore`,
`GroundworkWorkflowExecutionStateStore`, `GroundworkWorkflowHoldStateStore`,
`GroundworkWorkflowRuntimeAttentionQuery`, `GroundworkWorkflowSchedulerPoisonStore`,
`GroundworkWorkflowSchedulerWorkQueue`, `GroundworkWorkflowTestScopeStore`,
`GroundworkWorkflowTriggerBindingStore` — **plus** `GroundworkRuntimeCheckpointWriter`.

Beyond the runtime lane there are a further ten Groundwork persistence lanes with no EF counterpart
at all: Activities Design, Workflows Design, Workflows Publishing, Workflows Dashboard, Workflows
Runtime Distributed, Foundation Identity Persistence, Secrets, Studio Preferences, Diagnostics
(shared), and Elsa3 Import.

**Recommendation:** ADR 0004's "ten" should be corrected upstream, or re-scoped to name the ten it
means. Costing a differential per seam off "ten" understates the surface by more than half. That
said — see §1 — the correct number for *oracle* purposes is **three**, because that is how many
seams have an EF comparand at all.

---

## 4. Locks and leases — neither stack, and no EF exposure

`src/Elsa/Locking` contains exactly two projects, and the brief's suspicion is correct: **neither
EF nor Groundwork backs distributed locking.**

- **Contract:** `src/Elsa/Locking/Core/IDistributedLockProvider.cs` — `TryAcquireLock`,
  `TryAcquireLockAsync`, `AcquireLockAsync`.
- **Sole implementation:** `src/Elsa/Locking/FileSystem/` — `FileSystemLockingFeature.cs:33-37`
  registers a `DistributedLockProviderAdaptor` over Medallion's
  `FileDistributedSynchronizationProvider`, rooted at a configurable directory (default
  `App_Data/locks`).
- **Package:** `DistributedLock.FileSystem` 1.0.3 (`Directory.Packages.props:22`).

There are ~16 production consumers, including several *Groundwork* stores
(`GroundworkActivityUpgradePlanStore`, `GroundworkAddWorkflowDefinitionVersionCommand`,
`GroundworkPromoteDraftToVersionCommand`, `GroundworkActivityManagementProjectionWriter`, …), plus
`TaskExecutor` and the reconciler startup tasks.

**Answer for "remove EF completely": nothing to do.** Locking has no EF dependency to remove, no EF
oracle to harvest, and no Groundwork port pending. It is orthogonal to this program.

Two refinements worth recording so the question is not reopened:

1. **Leases are a different mechanism from locks, and are Groundwork-side.**
   `RuntimeExecutionLease` lives inside `ExecutionLivenessState`
   (`src/Elsa/Workflows/Runtime/Core/Models/ExecutionLivenessState.cs:56`) and is persisted through
   the Groundwork `ExecutionLivenessState` store, with lease fields indexed directly in the manifest
   (`ElsaRuntimeStorageManifest.cs:381-383`: `state.executionLease.ownerId`, `.acquiredAt`,
   `.expiresAt`). There is no EF counterpart, so leases have no oracle either — the same gap as
   every other runtime seam.

2. **The one EF-specific lock is EF-internal and dies with EF.**
   `src/Elsa/Persistence/EFCore/Services/MigrationsLockReclaimer.cs` reclaims stale
   `__EFMigrationsLock` rows, and only for SQLite (SQL Server's `sp_getapplock` and PostgreSQL
   advisory locks release on connection drop). This is EF migration machinery, not a persistence
   seam; it requires no Groundwork equivalent and no comparison.

---

## 5. Benchmark harness — exists, and is more built than assumed

A latency harness does **not** need standing up from zero. `benchmarks/` contains four projects:

| Project | Role |
|---|---|
| `Elsa.Groundwork.StorePerformance.Benchmarks` | The harness: gate evaluation, statistics, workload contract loader, artifact/matrix protocol, source provenance |
| `Elsa.Groundwork.StorePerformance.AdapterHost` | The child host binding frozen workload contracts to real provider drivers |
| `Elsa/Workflows/Runtime/Benchmarks` | Engine-level execution/concurrency benchmarks + durable round-trip diagnostics |
| `Elsa/Activities/Runtime/Benchmarks` | Activation-scope benchmarks, with committed results |

**The gate thresholds the brief quotes are already implemented.**
`benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/Harness/Gates.cs:43-46`:

```csharp
public static GatePolicy DefaultFor(GateClass gateClass) => gateClass == GateClass.RuntimeHotPath
    ? new(gateClass, 1.10, .90, 2.0, null, RatifiedDurableWritePathP95Milliseconds)
    : new(gateClass, 1.25, .80, 2.0, null);
```

The ordinary class is exactly p95 ≤ 1.25×, throughput ≥ 80%, p99 ≤ 2× — the diagnostics gate. The
runtime hot path is *stricter* (1.10 / 0.90 / 2.0) and additionally carries an absolute p95 ceiling.
Ratios are evaluated on medians across processes with confidence intervals (`Gates.cs:107-115`), and
gate replacement requires an independently reviewed record — self-authored amendments are rejected
(`Gates.cs:47`).

### The blocking gap: there is no EF adapter, anywhere

```bash
grep -rni "efcore\|entityframework" benchmarks/
# no matches
```

The harness is Groundwork-only by construction. Its README states the design principle — *"a missing
adapter is a blocked run, never a simulated result"* — and `matrix` refuses to start without
`--child-command`. Current adapter coverage, per
`benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/README.md`:

| Piece | State |
|---|---|
| CLI, run-request wire contract, native-plan staging | done, covered by offline tests |
| `capture-plan` for routeless workloads | done (`checkpoint-commit` is the only one) |
| `capture-plan` for workloads with native routes | **not started** — refuses rather than fakes |
| Adapter leaves | **`checkpoint-commit` only.** Every other workload is a blocked run |

So the latency baseline needs: (a) an EF adapter for the lanes where EF exists, and (b) route-capture
plus adapter leaves for the remaining workloads. Neither is a from-zero harness build.

The harness also has real operational sharp edges already documented and worth honouring rather than
rediscovering: every child re-verifies a clean `HEAD` with `--untracked-files=all` (so *any* edit
during a cohort aborts it — run from a dedicated checkout), the harness assembly is not
byte-deterministic so build-then-stage-then-run order is mandatory, and a cohort must be run
detached because it is tens of minutes of provider I/O and is deliberately not resumable.

---

## 6. Realistic workload — exists at two layers

A realistic start/resume/checkpoint workload exists. Two independent layers:

**(a) Frozen store-seam workloads.** Thirteen reviewed definitions under
`specs/094-harden-groundwork-stores/workloads/`, loaded and contract-validated by
`Contracts/WorkloadCatalog.cs` — which refuses to run unless the directory contains *exactly* the
thirteen, each matching its frozen handoff contract, input fingerprint and result digest
(`WorkloadCatalog.cs:63-67`, `:142-162`).

| Workload | Seam driven | Admission |
|---|---|---|
| `checkpoint-commit` | `IRuntimeCheckpointCommitStore`, fence validation | ready |
| `bookmark-lookup` | `IBookmarkStateStore` by workflow/stimulus/type | ready |
| `trigger-binding-stimulus-lookup` | trigger bindings + executable source references | ready |
| `recovery-scan` | execution/liveness/incident/scheduler/hold state | ready |
| `queue-drain` | `IWorkflowSchedulerWorkQueue` | ready |
| `outbox-drain` | `IRuntimePostCommitOutboxClaimStore` | ready |
| `due-timer-selection` | `IDurableTimerStore` | ready |
| `recurring-schedule-selection` | recurring trigger schedules | ready |
| `placement-takeover` | `IExecutionPlacementStore` | ready |
| `command-send-lease-ack` | distributed command transport | ready |
| `iam-normalized-lookup-update` | ASP.NET Core Identity user/role | ready |
| `diagnostics-durable-history` | Structured Logs + OpenTelemetry durable history | **blocked** |
| `secret-create-read-list` | tenant-scoped secrets | **blocked** |

These are not toy loops. `RuntimeCheckpointCommitWorkload` drives public runtime stores only,
acquires real ownership leases with fencing tokens, and is parameterized on `executionCount`,
`checkpointCount`, `activityChangesPerCheckpoint`, `durableValueChangesPerCheckpoint`,
`outboxEntriesPerCheckpoint`, `concurrentFenceContenders` and `payloadBytes`.

**(b) Engine-driven durable round-trip.** `benchmarks/Elsa/Workflows/Runtime/Benchmarks` runs real
workflows through the actual engine against **Groundwork SQLite** — not a mock:
`DurableRoundTripDiagnostics` and `BufferedCommitStageDiagnostics` construct a real
`SqliteDocumentStoreFactory` store and call `AddGroundworkRuntimeStores`. `EngineExecutionBenchmarks`
counts durable checkpoint-commit documents; `DurableRoundTripDiagnostics` counts drain-path
round-trips.

**What is missing is not a workload. It is a comparand.** Layer (b) has no EF side because no EF
runtime store exists. Layer (a) declares EF baselines that are not executable — see next section.

---

## The oracle is much smaller than the plan assumes

This is the finding that should drive re-costing.

Every one of the thirteen workloads declares an `efContractBaseline`. All thirteen carry
`"executionStatus": "not-executed"` and `"executionOwner": "#646"`. But they are not the same kind of
object:

| Baseline identity | Workloads | What it actually is |
|---|---|---|
| `frozen-runtime-*-ef-contract`, `frozen-distributed-*-ef-contract`, `frozen-ef-contract-snapshot` | 10 | *"Expected observable contract only"* — a hand-written description of what EF would do |
| `external-ef-comparison-required` | 1 (`secret-create-read-list`) | An external requirement; explicitly *"not a repository runtime dependency"* |
| `retained-sqlite-diagnostics-ef-oracle` | 1 (`diagnostics-durable-history`) | **A real, retained, runnable EF implementation** |

The ten `frozen-*-ef-contract` baselines describe seams — checkpoint commit, bookmark lookup, trigger
bindings, recovery scan, queue drain, outbox drain, due timers, recurring schedules, placement
takeover, command transport — **for which no EF implementation exists anywhere in the repository's
history of this tree.** Their own `purpose` fields say so obliquely: *"Expected observable
\<X\> contract only; #646 owns real same-provider EF execution and comparison."* But §1 establishes
that "real same-provider EF execution" is not merely un-run: it is **unwritable without first
building EF providers for seams the program exists to stop supporting.**

So the constraint "harvest the comparison before deleting the oracle" resolves to a much smaller
obligation than the plan implies:

- **Harvestable, and already harvested behaviourally:** OpenTelemetry diagnostics, Structured Logs
  diagnostics, ASP.NET Core Identity. Three seams. Six differential dimensions each. SQLite only.
- **Harvestable in principle, not yet:** the diagnostics *latency* comparison — the only workload
  with a real EF oracle and the only seam with a stated numeric gate.
- **Not harvestable, ever:** the ~25 runtime seams, the checkpoint writer, and the ten
  non-runtime Groundwork lanes. No EF code to compare against.
- **Not applicable:** locks (third-party), leases (Groundwork-only), MongoDB (structural).

Nothing here weakens the ordering constraint for the three seams it covers. It sharply narrows what
"the oracle" means, and it removes the implied blocker from the majority of the tree.

### The P0 seam's numeric gate is blocked — and there is a precedent for retiring it

The brief correctly identifies diagnostics as priority-0 because it is the one seam with a stated
comparison gate: p95 ≤ 1.25× EF, throughput ≥ 80%, p99 ≤ 2× EF
(`docs/reports/diagnostics-storage-workload.md:314`). Two facts complicate acting on it.

**First, that workload is explicitly blocked in code.** `diagnostics-durable-history` carries
`{"status": "blocked", "reason": "gate.diagnostics.absolute-budget-required"}`. The block is
defence-in-depth, not a soft flag: the reason code is code-owned
(`ReproducibleWorkloadScenarioCatalog.cs:16`), asserted by an architecture test
(`tests/Elsa/Architecture/GroundworkPerformanceHandoffTests.cs:161`), and — per
`specs/094-harden-groundwork-stores/quickstart.md:910` — a regression test *forges* a `ready`
admission and constructs the public `MatrixPlan` directly to prove the child is still never invoked.
The rationale: *"Numeric absolute budgets and an executable absolute-budget gate require independent
review before diagnostics measurement."*

**Second, the identical gate was already ratified away for a comparable lane, on fairness grounds.**
`specs/093-groundwork-design-persistence/contracts/design-persistence-contract.md:160` records a
program-owner decision of 2026-07-22 that replaced the same 1.25× / 80% / 2× EF ratio with absolute
budgets, because:

> *"the ratio compared semantically unequal work: the Groundwork write path executes the ratified
> operation-ledger marker, replay preflight, scope-bound sessions, and atomic multi-document staging
> per operation, while the temporary EF oracle performs bare `SaveChanges`."*

The EF oracle's own conformance profile declared the ledger, replay and storage-scope scenarios
**N/A**, so an EF ratio charges Groundwork for correctness work the oracle does not perform. Same-
provider EF measurements were retained as **recorded evidence, not a gate**.

The diagnostics gate is exposed to a weaker but real version of the same objection: the Groundwork
diagnostics adapter performs caller-supplied append-operation idempotency, provider-assigned
monotonic cursors, snapshot continuations that exclude backdated appends, and exact (not
approximate) counts — none of which `EfCoreOpenTelemetryStore` does. The 2026-07-12 workload report
itself notes the EF implementation *"performs several of these filters, de-duplication steps, and
joins after loading broad table sets"* and calls those *"implementation shortcomings, not portable
semantics"* (`diagnostics-storage-workload.md:107`).

**This is a decision for the program owner, not for the harness.** But the harness should not be
built on the assumption that the ratio gate will survive review, because an equivalent gate did not.

---

## Recommendation: harness shape and cost

The brief's prediction — *"if 1 and 2 hold, the oracle is mostly a fixture parameterized over DI plus
a workload driver"* — is right in form. Assumption 1 fails, which shrinks the scope; assumption 2
holds so completely that the behavioural half is already delivered.

### Behavioural differential — **effectively complete; do not rebuild**

All three EF-comparable seams already have a six-dimension, ledger-backed differential over a shared
target abstraction with `EfCore()`/`Groundwork()` comparands. Remaining work is verification, not
construction:

1. Run all three differential suites on current `main` and record the result. **~0.5 day.**
2. Read the three divergence ledgers and confirm every recorded divergence has a disposition, rather
   than assuming a green run means no divergence. **~0.5 day.**
3. Record in the #646 evidence chain that the behavioural differential is complete for all three
   seams and structurally impossible for the rest, citing §1. **~0.5 day.**

**Do not** extend the differential to runtime seams against in-memory and present it as EF-equivalent
assurance. It would be weaker evidence wearing the same label — precisely the confusion
`OpenTelemetryDifferentialTarget.cs:26` already warns against for Groundwork's conformance matrix.

### Latency baseline — one decision, then one adapter

> **Correction (2026-08-11): step 1 below misidentifies the blocking condition.** The block is not
> about whether the SQLite EF ratio survives review. Per
> [`performance-handoff.md`](../../specs/094-harden-groundwork-stores/contracts/performance-handoff.md),
> SQLite *has* the retained EF oracle and is gradeable under the existing default policy; the workload
> is blocked because `requiredProviders` is all four and **SQL Server, PostgreSQL and MongoDB** have no
> EF oracle and so need numeric absolute budgets that do not yet exist. The SQLite harvest — the only
> irreversible evidence — is blocked as collateral. The lever is therefore to narrow the workload's
> required provider set, not to re-litigate the ratio. See
> [`diagnostics-sqlite-split-basis.md`](../../specs/094-harden-groundwork-stores/contracts/diagnostics-sqlite-split-basis.md).
> The spec 093 fairness precedent below remains relevant to the *three deferred providers*' eventual
> budgets, but it does not gate the SQLite comparison.

The dependency order is a decision first, build second:

1. **Resolve the diagnostics gate question with the program owner** before building anything, using
   the spec 093 precedent as the reference decision. Either the 1.25× / 80% / 2× ratio stands, or
   diagnostics moves to absolute budgets with EF measurements recorded as evidence. **Blocking; ~1
   day of review, owner-scheduled.** *(Superseded by the correction above: this decision applies to
   the three non-SQLite providers, and is not a precondition for harvesting the SQLite oracle.)*
2. **If and only if the ratio survives:** build one EF diagnostics adapter leaf for the harness
   (SQLite, both diagnostics streams) and unblock `diagnostics-durable-history`. **~3–5 days**,
   including route capture, which `capture-plan` does not yet support for workloads with native
   routes. *(The "if and only if" condition is withdrawn — the adapter leaf is needed for the SQLite
   harvest regardless of how the three deferred providers are eventually graded.)*
3. **Independently of (1) and (2):** the eleven ready workloads still need adapter leaves to produce
   any number at all. Only `checkpoint-commit` has one. These are graded on absolute budgets and
   restart-recovery evidence, not EF ratios — `specs/144-zero-ef-final-removal/quickstart.md:483`
   already states that rows without a comparand *"cannot receive an EF-ratio verdict and must be
   graded on absolute budgets."* **This is the bulk of remaining #646 work and it is not oracle work
   at all.** It does not gate EF deletion, and should be tracked separately so it stops appearing to.

### What this changes about the ordering constraint

The constraint should be restated with its true scope, because as written it implies a repo-wide
blocker that does not exist:

> Deleting the **diagnostics** and **ASP.NET Core Identity** EF implementations must follow their
> behavioural differential (done) and the resolution of the diagnostics latency gate (open). The
> ~25 runtime seams, the checkpoint writer, the ten non-runtime Groundwork lanes, locking, and
> MongoDB have no EF oracle and are not gated by oracle harvesting — they are gated by absolute-
> budget evidence, which is a different obligation with a different owner.

`specs/144-zero-ef-final-removal/ef-removal-inventory.md:144-163` already encodes the deletion DAG
correctly in this shape (diagnostics and OpenIddict leaves first, Identity after its benchmark-oracle
obligation, shared substrate last). This report supplies the evidence for *why* the non-diagnostics
branches carry no oracle dependency.

### One caution on the inventory

`ef-removal-inventory.md:165-172` warns that
`benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/` is a load-bearing temporary consumer of the
frozen Identity EF oracle's observable results, and that absence of a baseline row is not evidence of
removability. That warning is correct and should be preserved. Note the mechanism precisely, though:
the harness consumes the Identity oracle's **frozen observable results** through the workload
contract's fingerprints and digests, not through an EF code reference — `grep` for EF across
`benchmarks/` returns nothing. So T047's disposition is about retiring frozen *evidence* identities,
not deleting EF adapter code that does not exist there.

---

## Corrections to `diagnostics-storage-workload.md`

The brief asked for this report's stale grouped-reduction line to be corrected. **That correction
already exists and is accurate.** It was landed as "Amendment 2026-07-31: grouped reduction is
required" (`docs/reports/diagnostics-storage-workload.md:9-28`) and records the call sites, the ten
reducers, the grouping key, ordering, page size, and the near-miss removal — exactly the content
requested. All three stale statements (Outcome `:40`, Out of Scope `:218-219`, Decision `:358`) carry
supersession pointers.

I verified the amendment against source rather than assuming it. Every citation is still exact at
this head:

| Amendment claim | Verified |
|---|---|
| `GroundworkOpenTelemetryStore.cs:453` — `QueryTracesAsync` grouped call | ✅ `QueryGroupsAsync` at :453, method at :418 |
| `GroundworkOpenTelemetryStore.cs:490` — `GetTraceAsync` grouped call | ✅ `QueryGroupsAsync` at :490, method at :480 |
| `OpenTelemetryRecordStreamDefinitions.cs:142` — `TraceSummaryProfile` | ✅ `CreateTraceSummaryProfile()` at :142 |
| Bounded input `MaxGroupedQueryInputRecords` at `:127` | ✅ `:127`, set to `MaxTraceRecordCapacity` |
| Grouping key `TraceId`; ordering `StartTime` | ✅ `:144`; `Names(RecordFields.StartTime)` at `:169` |
| Ten reducers, exactly as listed | ✅ `:146-158`, all ten match kind-for-kind and field-for-field |

Two precision gaps remain, which this PR fixes rather than leaving to drift:

1. The amendment lists the post-reduction predicates but omits `StartTime` `RangeInclusive`
   (`:166`), which is load-bearing for the trace list's inclusive start-time range filter.
2. It says *"page size is the caller's `take`"* without recording the declared ceiling on that take,
   `MaxTake: 5_000` (`:170`). The brief asked for page size as a **named** requirement, so the cap
   belongs in the record alongside the caller-supplied value.

## Follow-ups this report does not take on

- ADR 0004's "ten runtime persistence seams" needs correcting upstream in `valence-works/groundwork`
  (not readable from this session). §3 supplies the counts and method.
- The diagnostics gate decision in §"Recommendation" step 1 is a program-owner call.
- Adapter leaves for the eleven ready workloads are #646 delivery work, not oracle scoping.
