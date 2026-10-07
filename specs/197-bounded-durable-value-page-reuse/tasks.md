# Tasks: Bounded Durable-Value Page Reuse

**Input**: Design documents in `specs/197-bounded-durable-value-page-reuse/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/page-reuse.md`, and `quickstart.md`.

**Test cadence**: The program lead selects regression-first verification for this bounded change under the owner's delegated implementation and QA authority. Establish the non-empty EF baseline first; add each behavior regression before or alongside its implementation and prove the reduction/safety assertions bite through a before/fixed or focused revert/mutation comparison. Existing tests remain intact. This is a work-unit choice, not a change to the application's constitution, and no owner decision is outstanding.

**Organization**: Tasks are grouped by the three P1 user stories. Setup and shared prerequisites have no story label. The stories share a bounded memo and are sequenced for safe integration rather than treated as independently shippable changes.

## Phase 1: Setup

**Purpose**: Establish the baseline and proof oracle before adding reuse.

- [x] T001 Prepare the deterministic non-empty normal typed-start → deferred `ActivityStarted` → invoke fixture in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfDurableValuePageReuseTests.cs`; retain its uncached backing-page count and serialized inputs, identity, visibility and result before adding the memo. Reuse the existing EF fixture and a narrow test counter. This baseline becomes T010's same-cadence enabled/disabled oracle.

---

## Phase 2: Foundational Work

**Purpose**: Add the same-cadence control, memo primitive, and fail-closed eligibility used by every story.

- [x] T002 [P] Add `CoalesceDurableValueReads` to `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistenceOptions.cs` and `src/essentials/Workflows/Runtime/Api/Coalescing/WorkflowsRuntimeCheckpointPersistenceFeature.cs`; preserve it and `CoalesceInspectionReads` when `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs` applies a per-session cap.
- [x] T003 Update `tests/essentials/Workflows/Runtime/Tests/WorkflowsRuntimeCheckpointPersistenceFeatureTests.cs` for the setting's manifest registration/default, Immediate pass-through, Coalesced enabled/disabled values, and per-session cap preservation of both booleans.
- [x] T004 [P] Implement the session-local `public sealed` page memo in `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDurableValuePageMemo.cs` with exact request/context/codec-reference keys, detached row/page cloning, checked 32-page/1,024-row/4-MiB content accounting, successful-empty-page handling, and complete uncached overflow fallback.
- [x] T005 Cover every logic branch of `RuntimeCoalescingDurableValuePageMemo` in `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageMemoTests.cs`, including deep JSON/metadata copies, checked overflow, and complete results at each capacity boundary.
- [x] T006 After T002 and T004, capture reuse eligibility in `src/essentials/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs` before decoration and pass it to the durable-value wrapper. Require one unambiguous `EntityFramework` backend that owns the current contract and effective concrete registration, plus the effective stable built-in HMAC codec composition; prove the memo uses the same singleton codec instance the inner EF store resolves. Custom, in-memory, ambiguous or overridden backend compositions, codecs registered as transient/scoped, or otherwise unprovable compositions must bypass without a new startup failure.
- [x] T007 Cover each eligibility branch in `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageReuseEligibilityTests.cs`, including replacement of the effective concrete registration and codec compositions that cannot prove identity with the inner store.

T002–T003 are accepted. The session snapshots both boolean settings; all four combinations survive actual authored-cap scope creation without changing host options. The feature suite passes 13/13, and the combined feature/inspection/memo suite passes 29/29. The existing inspection wrapper still uses its original options path; this checkpoint does not change inspection behavior.

T004–T005 are accepted as a standalone primitive after root and independent review, 14 memo tests, and a focused mutation that fails when the pre-clone cumulative budget check is removed. The combined configuration/memo suite passes 24/24 after restoration. [Evidence and limits](evidence/memo-primitive-verification.md) distinguish reachable branch proof from defensive arithmetic guards. No store/session integration or query reduction is established by this checkpoint.

T006 is accepted on the integrated candidate: the unique singleton HMAC codec is resolved through the same scoped provider for the inner EF store and wrapper, and actual eligible EF reuse executes that path. T007 is accepted after 64 eligibility cases, root's complete 2,125-test Runtime pass, 100% registration-class line and branch coverage, independent review and a failed-recapture mutation that fails as expected. A validated descriptor map now represents successful capture without a redundant parallel flag; immutable-construction and already-rejected duplicate guards were simplified, while live registration checks and exception fallback remain tested. The earlier [eligibility checkpoint](evidence/eligibility-checkpoint.md) is historical; [integration evidence and remaining limits](evidence/integration-verification.md) supersede its runtime-integration status.

**Checkpoint**: The option, memo primitive, and safe eligibility decision are tested before integration into the read path.

---

## Phase 3: User Story 1 — Reuse unchanged durable-value pages (Priority: P1)

**Goal**: Reuse equivalent raw EF pages during a normal non-empty typed start-to-invoke flow with identical workflow-visible values and results.

**Independent Test**: Run typed start → deferred `ActivityStarted` → invoke with reuse enabled and disabled under identical Coalesced settings. The actual first-party EF composition must be eligible; the enabled run must issue fewer backing durable-value page requests and produce byte-equivalent serialized inputs, identity, variable visibility, and final result.

- [x] T008 [US1] Integrate the memo into `CoalescingDurableValueStateStore` in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs`: look up only by the complete exact inner request and current context/codec identity, admit only successful complete inner pages, and re-run the existing staged-page merger for every logical read.
- [x] T009 [US1] Add focused wrapper tests in `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageReuseTests.cs` for equivalent raw-page hits, current-overlay re-merge, byte-equivalent results, empty successful pages, and no caching of exceptions or rejected cursors. On a would-be hit, cancellation, identity validation, and live missing/changed-scope checks must remain authoritative; if they cannot be performed through a safe shared path without duplicating EF cursor/identity internals, bypass reuse and call the provider. Include negative hit-path assertions.
- [x] T010 [US1] Add the non-empty normal-path enabled/disabled integration oracle in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfDurableValuePageReuseTests.cs`, using the registered EF-backed store and actual shared stable codec composition rather than a cache-only wrapper. Count backing page queries with test-only interception or an equivalent narrow counter. Label the result as fixture evidence only; do not claim that it proves primary HTTP or REST query savings.

**Checkpoint**: US1 passes only with a real eligible EF composition, deterministic reduction, and semantic equivalence.

---

## Phase 4: User Story 2 — Observe staged and persisted changes immediately (Priority: P1)

**Goal**: Preserve the current overlay on every read and prevent old pages from surviving actual provider writes or late completion.

**Independent Test**: Read and hit a baseline page, stage an upsert/delete, then verify the merged result; exercise inner checkpoint and direct writes, including failure/cancellation and a read that overlaps a write.

- [x] T011 [US2] Fence memo generations in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs` and `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs` before and in `finally` around actual inner checkpoint commits and direct durable-value `SaveAsync`/`DeleteAsync`; count overlapping writes, bypass hits/fills while a write is active, preserve deferred `BufferDeferred` without invalidation, and reject late fills. A failed/cancelled write permanently disables admissions for that session; do not let a later successful write reactivate it.
- [x] T012 [US2] Extend `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageReuseTests.cs` to cover staged additions/replacements/tombstones, deferred-buffer no-invalidation, successful direct and checkpoint write invalidation, failed/cancelled write permanent disable, overlapping reads/writes, and old-generation completions that cannot refill the memo.

**Checkpoint**: Cached provider data cannot hide staged changes or cross an actual write freshness boundary.

---

## Phase 5: User Story 3 — Preserve isolation and safe fallback (Priority: P1)

**Goal**: Keep reuse inside eligible live ownership and preserve uncached outcomes at access, identity, codec, nesting, disposal, recovery, or capacity boundaries.

**Independent Test**: Exercise changed/missing scopes, access/identity rejection, codec replacement, non-EF and overridden registrations, nested/disposed sessions, recovery, and capacity limits; compare values, exceptions, cancellation, and provider outcomes with reuse disabled.

- [x] T013 [US3] Make nested and ended ownership permanent memo-disable boundaries in `src/essentials/Workflows/Runtime/Services/Coalescing/AsyncLocalRuntimeCoalescingSessionAccessor.cs` , `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs`, and `src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs`: suspend the parent for the rest of its scope, allow a legitimate child its own fresh eligible memo, and clear memo-only state on disposal/deactivation/interruption/recovery without changing overlay or cancellation behavior. Bind cache invalidation to the actual drain cancellation token in `src/essentials/Workflows/Runtime/Services/Scheduler/WorkflowDrainOrchestrator.cs` so lease-loss cancellation fences reuse; callbacks must touch only synchronized memo state. Capacity overflow may disable only until a successful boundary/reset; keep this distinct from permanent owner/write disablement.
- [x] T014 [US3] Complete negative eligibility and ownership tests in `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageReuseEligibilityTests.cs` and `tests/essentials/Workflows/Runtime/Tests/CoalescingDurableValuePageReuseTests.cs`: prove current effective descriptor ownership, actual shared stable codec identity, different-instance/custom-codec bypass, Immediate/in-memory/custom/ambiguous fallback, live scope changes, nested parent non-reactivation, independent child behavior, disposal/recovery clearing, actual drain-token/lease-loss cancellation disabling reuse without changing the staged overlay, and bounded complete-result fallback without truncation.

**Checkpoint**: US3 passes only when a cache hit cannot skip current authorization, identity, cursor rejection, owner, or provider behavior.

T008–T014 are accepted after root and independent review, 2,125 Runtime tests and 904 EF integration tests. T014 adds `Persisted_interruption_snapshot_recovers_memo_populated_coalesced_execution` in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfDurableValuePageReuseTests.cs`: real persisted interruption state is recovered by a fresh EF host through the production resumption service, with fresh page reads, expected completion and active stale-fence rejection. This is a database-snapshot recovery proof, not an OS process-kill test; the live committed-bookmark restart is separate evidence. See [the measured reduction, causal regressions and remaining delivery gates](evidence/integration-verification.md).

---

## Phase 6: Polish and Cross-Cutting Verification

**Purpose**: Validate preserved provider behavior and all required correctness gates.

- [ ] T015 Run `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`, and `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj`; retain the affected recovery, partition-isolation, feature-registration and branch-coverage results for the candidate. Run the architecture guard at `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj` and generated-maps check `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. Preserve any failure or unavailable gate as such.
- [ ] T016 Rebuild the owned PostgreSQL host and run `e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1` and `e2e-tests/http/Capture-RuntimeDbRest2386Control.ps1` using the arguments in `specs/197-bounded-durable-value-page-reuse/quickstart.md`; confirm HTTP `200` with `Alice Smith`, expected REST output, and terminal workflow state. Also run the bounded four-client Coalesced response/settlement control and relevant interruption/recovery and malformed-input cases, retaining per-attempt identity and all outcomes; reconcile any reproduced valid-input failure with T03/#2388 before acceptance. Report these as correctness regressions, not evidence of primary-workload page-call savings.
- [ ] T017 Review the final feature diff against `specs/197-bounded-durable-value-page-reuse/contracts/page-reuse.md` and `specs/197-bounded-durable-value-page-reuse/quickstart.md`; verify no public paging/store changes, no schema change, no Immediate behavior change, and no latency or primary HTTP/REST savings claim inferred from the deterministic fixture. Retain the before/fixed or revert/mutation bite-proof; record exact candidate/source/configuration, all required gates and final diff review in the PR. Update the runtime coalescing operator guidance with the flag, eligibility, bounds and fallback, and refresh affected maps explicitly if needed.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: T001 establishes the uncached fixture and baseline used by T010; test ordering is already resolved above.
- **Foundational (Phase 2)**: T002 and T004 can proceed in parallel after T001; T003 follows T002, T005 follows T004, and T006–T007 follow both branches.
- **US1**: T008 follows T006–T007; T009 follows T008; T010 follows the wrapper and focused tests.
- **US2**: T011 follows US1's working cache path; T012 follows the actual-write fencing.
- **US3**: T013 follows the integrated write lifecycle; T014 follows T013.
- **Polish**: T015–T017 depend on all three user stories.

### Parallel opportunities

After T001, T002 (option/feature files) and T004 (new memo primitive) are independent and may proceed in parallel. Within each story, develop the affected regression before or alongside the implementation, preserving the uncached control and recording bite-proof. No safe same-story parallel edit pair exists because tasks share the memo, wrapper, or test fixture files.

## Implementation Strategy

US1's non-empty EF enabled/disabled scenario is the smallest useful reduction proof. It is not safe to ship or enable reuse until US2 write fencing and US3 fallback/ownership gates pass. The implementer can pause after T010 to assess the reduction signal, but must complete T011–T017 before delivery. No task authorizes a default checkpoint-mode change, a new telemetry platform, or a performance guarantee.

## Notes

- `[P]` marks only T002 and T004, which use separate files and have no dependency on each other.
- Task IDs follow execution order; `[US1]`–`[US3]` map to the stories in `spec.md`.
- Framework policy requires continuity of existing tests; this program additionally requires affected registration/behavior coverage and before/fixed or mutation bite-proof. No new owner approval is required for this verification choice.
- A failed/cancelled write, nested parent, and ended owner permanently disable that memo. Capacity overflow disables admission only until a successful boundary/reset.
