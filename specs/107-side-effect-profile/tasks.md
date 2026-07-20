# Tasks: Author-Declared Side-Effect Profile

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Ordering: contract model → attribute → publish-time compiler → metadata key → claimer → callers → policy → built-ins → spec-095 amendment → tests.

## Phase 1 — Contract model & attribute

- [x] **T001** Add `SideEffectProfile { External, ReplaySafe }` enum; add the member + both-constructor params (default `External`) + enum validation to `ActivityContract`. (FR-001)
- [x] **T002** Make the profile participate in `SchemaFingerprint` only when non-default. (FR-002, D3)
- [x] **T003** Add `[ActivitySideEffectProfile(SideEffectProfile)]` class attribute. (FR-003, D2)

## Phase 2 — Publish-time fold

- [x] **T004** `ExecutableNodeCompiler.BuildActivityContract`: reflect the attribute, fold the profile into the pinned contract. (FR-003)
- [x] **T005** `ActivityTemplatePlacer.StampBoundaryContract`: carry `contract.SideEffectProfile` through the clone. (FR-003)

## Phase 3 — Transport & claimer

- [x] **T006** `RuntimeMetadataKeys`: add `CheckpointSideEffectProfile` key + value constants. (FR-004)
- [x] **T007** `ActivityAttemptActivationClaimer`: thread the profile into the claim entry points + `ClaimAsync`; stamp the metadata key; keep Mandatory. (FR-004, D4)
- [x] **T008** `WorkflowInvokeActivitySchedulerWorkHandler` + `WorkflowParentActivityCompletionSchedulerWorkHandler`: pass the node contract's profile. (FR-004)

## Phase 4 — Policy

- [x] **T009** `CoalescingRuntimeCheckpointPersistencePolicy`: remove `ActivityAttemptClaimed` from the unconditional set; conditional `Immediate`/`Deferred` on the profile metadata; keep the coalesced-flush marker + other mandatory names. (FR-005, FR-007)

## Phase 5 — Built-ins & spec amendment

- [x] **T010** Declare `If`/`Sequence`/`Flowchart`/`For`/`ForEach`/`While`/`Do`/`Switch`/`Parallel` `ReplaySafe`; leave `WriteLine` + the reusable boundary `External`. (FR-006)
- [x] **T011** Amend spec-095 FR-019 (and FR-020/FR-022 wording) so the pre-activation *flush* is profile-conditional; identity is always written. Note ADR 0032 R2 as the authority.

## Phase 6 — Tests

- [x] **T012** Extend `RuntimeCheckpointCoalescingPolicyTests`: `ActivityAttemptClaimed` conditional (External⇒Immediate, absent⇒Immediate, ReplaySafe⇒Deferred); other mandatory names stay Immediate.
- [x] **T013** Contract-fingerprint tests: profile changes fingerprint; default is stable + equals profile-unaware path; JSON round-trip validates a ReplaySafe contract.
- [x] **T014** Compiler test: `[ActivitySideEffectProfile(ReplaySafe)]` → pinned contract profile is ReplaySafe; unmarked → External.
- [x] **T015** Prove the store-level buffer-vs-flush routing in `RuntimeCheckpointCoalescingTests`: a `Deferred` ReplaySafe `ActivityAttemptClaimed` buffers into the overlay (nothing durable) and folds forward into the terminal flush; the existing `Immediate` boundary test covers External. (Placement note in plan.md: `GroundworkCoalescingCrashConvergenceTests` uses a contractless node and cannot exercise the claim path; the crash-convergence mechanism for deferred checkpoints is already covered there and a ReplaySafe claim now rides that same path.)
- [x] **T015b** Serialize the profile with `[JsonIgnore(WhenWritingDefault)]` so a default-External contract's `workflowExecutable` golden is byte-identical; verified by the Groundwork fixture suite. (FR-002)
- [x] **T016** Run the spec-095 attempt/poison suites unchanged; run the four full test projects; report totals.
