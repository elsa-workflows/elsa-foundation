# Tasks: Single Durable Transaction per Drain Step

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Ordering: models → contracts/accessor → queue consume → committer → stores → fold → drainer → DI → tests.

## Phase 1 — Models & contracts

- [x] **T001** Add `Core/Models/ConsumedSchedulerWorkItem.cs` (record + `FromClaim`). (FR-001)
- [x] **T002** Extend `Core/Models/RuntimeCheckpointCommit.cs`: `ConsumedSchedulerWorkItems` + constructor param + `WithConsumedSchedulerWorkItems`. (FR-001)
- [x] **T003** Extend `Core/Models/RuntimeCheckpointCommitResult.cs`: `ConsumedSchedulerWorkItemIds`. (FR-002, D5)
- [x] **T004** Extend `Core/Models/RuntimeCheckpointCommitFingerprint.cs`: include consumed items only when present. (FR-008)
- [x] **T005** Add `Core/Contracts/IRuntimeConsumedSchedulerWorkClaimAccessor.cs` + `Services/RuntimeConsumedSchedulerWorkClaimAccessor.cs`. (FR-003)
- [x] **T006** Add `Core/Exceptions/RuntimeSchedulerWorkConsumeConflictException.cs`. (FR-007)
- [x] **T007** Add `ConsumeClaimedAsync` default to `Core/Contracts/IWorkflowSchedulerWorkQueue.cs`. (FR-004)

## Phase 2 — Providers

- [x] **T008** `Services/InMemoryWorkflowSchedulerWorkQueue.cs`: fence-checked `ConsumeClaimedAsync`. (FR-004)
- [x] **T009** `Persistence/Groundwork/Stores/GroundworkWorkflowSchedulerWorkQueue.cs`: fence-checked `ConsumeClaimedAsync`. (FR-004)

## Phase 3 — Committer & commit stores

- [x] **T010** `Services/RuntimeCheckpointCommitter.cs`: attach consume-change (session-aware suppression), guard, mark accessor. (FR-002, FR-005)
- [x] **T011** `Services/InMemoryRuntimeCheckpointCommitStore.cs`: apply consume in UoW; return + record ids. (FR-004, FR-008)
- [x] **T012** `Persistence/Groundwork/Stores/GroundworkRuntimeCheckpointWriter.cs`: apply consume in UoW; validate; marker records ids. (FR-004, FR-008)

## Phase 4 — Fold & drainer

- [x] **T013** `Services/Coalescing/RuntimeCheckpointFold.cs`: union `ConsumedSchedulerWorkItems`. (FR-005)
- [x] **T014** `Services/WorkflowSchedulerDrainer.cs`: set/read accessor; skip ack on atomic path; consume-conflict → claim-lost. (FR-006, FR-007)

## Phase 5 — DI

- [x] **T015** `Extensions/RuntimeCoreServiceCollectionExtensions.cs`: register accessor; thread into drainer + committer. (FR-003)

## Phase 6 — Tests

- [x] **T016** New tests: (a) consume iff commit lands; (b) stale-claim claim-lost; (c) handler-fault legacy ack + poison once; (d) coalesced fold union.
- [x] **T017** Run full runtime + Groundwork persistence test projects; report totals.
