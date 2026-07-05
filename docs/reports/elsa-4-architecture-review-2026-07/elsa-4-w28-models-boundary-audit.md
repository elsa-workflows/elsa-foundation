# W28 — Per-Type Deep Audit of `Elsa.Workflows.Runtime.Core/Models/`

> **Status: RATIFIED.** The user ratified the Models/ boundary on **2026-07-05**: **move nothing —
> all 77 files stay in `Elsa.Workflows.Runtime.Core`**, on the three anchors documented below
> (A wire-format, B contract-surface, C cohesion/no-benefit). This report is the Stage-1 evidence
> for that decision and the input to the ADR 0033 execution unit (W28 Stage 2).

**Scope:** 77 files, 206 public types, 6,356 LoC in `src/Elsa/Workflows/Runtime/Core/Models/` at
`0b49be0e`. Every file read in full; every enum's ordinals read; every `[JsonConstructor]` payload
traced to its deserialization site; reference maps built by grep across all of `src/` and `tests/`.

## Delta from the inherited Part-1 verdict

The predecessor's headline — "73/77 wire/contract-adjacent (stay); 4 carry logic; move nothing" —
is **confirmed in its recommendation** but corrected on two claims:

1. **The "4 logic-bearing files" figure undercounts by an order of magnitude.** Logic (validating
   constructors, static factories, computed properties, defensive snapshotting, id-builders,
   merge/inference methods) is the *norm* in this directory: **~55 of 77 files contain non-trivial
   behavior.** This does not change the split line — constitution §2.1 makes *models*
   charter-legitimate in a `.Core`, and a validating record is still a model — but the boundary was
   ratified knowing the real logic density.
2. **A previously-unnamed cluster of ~9 pure engine-plumbing types exists** (no persistence, no
   contract-interface exposure, no cross-boundary consumer; produced and consumed only inside
   `Services/`). Verdict is still STAY for all of them, on anchor C below, but they are named
   explicitly rather than folded into an undifferentiated "73".

No fifth logic-bearing type changes the split; no type is engine-only in a way that forces a move.

## Split-side legend (verified from `.csproj` refs + ADR 0033)

- **STAYS in `.Core`:** `Models/`, `Contracts/`, `Constants/`, `Middleware/` (contract types),
  `Builders/`, `Validators/`, `Exceptions/`.
- **MOVES to new `Elsa.Workflows.Runtime`:** `Services/` (96 files), `Resolvers/` (2 files),
  `Extensions/` (composition root).
- **Engine-side consuming projects** (take a ref to the new package): `Activities.Runtime`,
  `Publishing.Api`, `Distributed`, `Flowchart`, `Runtime.Api`, `Scheduling`, `Resumption`
  (+ `Runtime.JavaScript` pending build verification).
- **Contract-side consuming projects** (keep `.Core` ref only): the 6 ControlFlow/Sequence
  Navigator-hosting activity projects, `Persistence.Groundwork`, `Publishing.Core`,
  `Expressions.JS.Jint`, other `Activities.*`.
- **`tests/Elsa/Workflows/Runtime/Tests/`** directly instantiates 19+ engine `Services/` types →
  follows the engine; its coverage does NOT anchor a type to `.Core`.

## §E6 enum-ordinal pinning — established facts (verified against fixtures)

- **Encoding:** all runtime state serializes via `GroundworkRuntimeDocumentSerializer.Options`
  (`JsonSerializerDefaults.Web`, **no `JsonStringEnumConverter`**) → **every enum is an ordinal int
  on the wire**. Distributed transport uses the same ordinal encoding
  (`executionCommandTransport.json`: `kind:1`, `deliveryMode:1`).
- **Golden fixture set:** 16 groundwork fixtures (`tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v1/`)
  + 2 distributed wire fixtures (`tests/Elsa/Workflows/Runtime/Distributed/Tests/Fixtures/v1/`).
  Version-pinned via `ElsaRuntimeDocumentVersions` (all kinds at v1) with a fixture-drift test.
- **Pinned by a populated golden value:** `ActivityExecutionStatus` (activityExecutionState.json
  `status:1`), `IncidentSeverity/Status/ResolutionAction` (incidentState.json `2/0/0`),
  `DurableValueLifecycle/Storage` (durableValueState.json `1/1`), `WorkflowExecutionCommandKind`
  (schedulerWorkItem.json `commandKind:3`; executionCommandTransport.json `kind:1`),
  `RuntimePostCommitOutboxStatus` (postCommitOutbox.json `status:0`), `RecurringScheduleKind`
  (recurringTriggerSchedule.json `kind:0`), `WorkflowExecutableScope` + `RuntimeInputBindingSource`
  (workflowExecutable.json `scope:0`, `source:3`), `WorkflowExecutionCommandDeliveryMode`
  (executionCommandTransport.json `deliveryMode:1`), `WorkflowExecutionStatus`
  (workflowExecutionState.json `status:3`).
- **Wire-frozen but NOT captured by a populated golden value** (the fixture serializes a *minimal*
  instance): the `RuntimeCheckpointCommit` state-change enums `RuntimeStateCategory` /
  `RuntimeStateChangeOperation` / `RuntimeWaitDependentIntentFailurePolicy` (checkpointCommit.json
  is `{commitId, occurredAt, workflowExecutionId}` only); the `WorkflowHoldState` enums
  `WorkflowHoldScope/Status`, `RuntimePauseContinuationPolicy`, `RuntimeIngressSourceKind`,
  `IngressPauseBehavior`, `RuntimePauseBoundary` (controlPlaneState.json has **empty holds
  arrays**). These ordinals are guarded by round-trip contract tests in
  `tests/Elsa/Workflows/Runtime/Tests/`, not a golden snapshot.

Every enum ordinal in this directory is a durability/wire contract regardless of split: an enum
whose ordinal is a persisted format is a model, and models stay.

## The audit table

Classification: **(a)** wire/contract-adjacent → STAYS · **(b)** engine-internal working state →
move candidate · **(c)** ambiguous. "Referenced by" names production consumers; contract-side
anchors in **bold**.

### Group 1 — Activity/inspection execution state & projections

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| ActiveActivityOutput.cs | `ActiveActivityOutput`, `ActiveActivityOutputKey` | 62 | a | Execution-local output holder + composite equality-key (custom `Equals`/`GetHashCode`); explicitly "not durable" | none | Services, **Contracts**, Activities.Runtime |
| ActivityExecutionInspectionProjection.cs | `ActivityExecutionInspectionProjection`, `ActivityExecutionInspectionSummaryProjection` | 173 | a | Committed-inspection projection DTOs with `FromState`/`Merge`/`FromProjection` factories (metadata-merge, dedupe-by-id) | feeds persisted `activityExecutionInspection.json` | Services(11), **Contracts**(3), **Persistence**, Activities.Runtime, Runtime.Api, Flowchart |
| ActivityExecutionInspectionSummaries.cs | `ActivityExecutionBookmarkSummary`, `ActivityExecutionIncidentSummary` | 51 | a | Summary DTOs with `From(...)` mapping factories | part of inspection projection wire | Services, **Contracts**, Runtime.Api, Activities.Runtime |
| ActivityExecutionInspectionValueSnapshot.cs | `ActivityExecutionInspectionValueSnapshot` + `ActivityExecutionInspectionValueSubject` (enum 0–2) | 42 | a | Value-snapshot DTO w/ `FromDecision` factory; enum ordinals ride the inspection projection | enum pinned via inspection projection persistence; not a standalone golden | Activities.Runtime(5), Services, **Contracts** |
| ActivityExecutionState.cs | `ActivityExecution`, `ActivityExecutionState`, `ActivityExecutionStatus` (enum 0–7) | 98 | a | Persisted durable-state record; `[JsonConstructor]` on primary ctor | **golden `activityExecutionState.json` (`status:1`)** | Services(17), Activities.*(8), **Contracts**(3), **Persistence**, Flowchart |
| ActivitySchedulingProvenance.cs | `ActivitySchedulingProvenance` | 48 | a | Correlation record (why/where scheduled); `Empty` singleton + `From` factory; embedded in ActivityExecutionState | rides activityExecutionState wire | Activities.*(5), Services, **Contracts**, Runtime.Api, Flowchart |
| RecordedActivityOutput.cs | `RecordedActivityOutput` | 18 | a | Recorded-output value holder (validating ctor) | none direct | Activities.Runtime, Services |
| RuntimeOutputCapture.cs | `RuntimeOutputCapture` | 43 | a | Durable output→durable-value promotion declaration (validating ctor); part of compiled `ExecutableNode` | rides workflowExecutable wire | Publishing.Api (compiler), Activities.Runtime |

### Group 2 — Bookmarks, durable values, incidents (persisted state)

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| BookmarkState.cs | `BookmarkState` | 19 | a | Persisted durable resume-handle record (pure positional) | **golden `bookmarkState.json`** | Services(8), **Persistence**, **Contracts**, Resolvers |
| BookmarkConsumptionCheckpointModels.cs | `BookmarkConsumptionCheckpointRequest`, `BookmarkConsumptionCheckpointResult` | 131 | a | Request w/ heavy cross-state validation (deserializes `RuntimeCompleteActivityCommandPayload` to cross-check) + result w/ computed `CheckpointName`/`PersistenceDecision` | none (transient) | Services, **Contracts**, Activities.Runtime |
| BookmarkResumeModels.cs | `BookmarkResumeRequest`, `BookmarkResumeResolution` | 15 | a | Resolver request/result records (pure positional) | none | Services, Resolvers, **Contracts** |
| BookmarkResumeDispatchModels.cs | `BookmarkResumeDispatchRequest`, `BookmarkResumeDispatchResult`, `RuntimeResumeBookmarkCommandPayload`, `BookmarkResumeDispatchStatus` (enum 0–8) | 151 | a | Dispatch req/result w/ status-conditional invariants; `RuntimeResumeBookmarkCommandPayload` is a wire payload deserialized from `RuntimeSchedulerWorkItem.Payload` (`WorkflowResumeBookmarkSchedulerWorkHandler.cs:303`) | payload rides schedulerWorkItem wire; enum via contract test | Services, Scheduling; payload: Activities.Runtime |
| BookmarkStimulusLookupModels.cs | `BookmarkStimulusLookupRequest`, `BookmarkStimulusLookupResult`, `BookmarkStimulusLookupStatus` (enum 0–2) | 77 | a | Lookup req/result w/ status-conditional invariants (ambiguous requires ≥2 ids) | enum via contract test | Services(2), **Contracts** |
| GlobalBookmarkStimulusLookupModels.cs | `GlobalBookmarkStimulusLookupRequest`, `GlobalBookmarkStimulusLookupResult` | 63 | a | Global lookup req + result w/ computed `HasMatches`/`WorkflowExecutionIds` + `CorrelationMatches` helper | none | Services(2), **Contracts** |
| DurableValueState.cs | `DurableValueState`, `RuntimeValueTypeDescriptor`, `DurableValueExternalReference`, `DurableValueLifecycle` (0–4), `DurableValueStorage` (0–3) | 121 | a | Persisted durable-value record w/ lifecycle/storage consistency `Validate`; type descriptor is the compiled value-type wire shape | **golden `durableValueState.json` (`lifecycle:1`, `storage:1`)** | Services(11), Activities.Runtime, **Persistence**, **Contracts** |
| IncidentState.cs | `IncidentState`, `IncidentHistoryProjection`, `IncidentSeverity` (0–3), `IncidentStatus` (0–3), `IncidentResolutionAction` (0–5) | 156 | a | Persisted continuation-state record w/ terminal-status invariants + observation projection | **golden `incidentState.json` (`severity:2`, `status:0`, `resolutionAction:0`)** | Services(3), **Persistence**, Runtime.Api, Activities.Runtime, **Contracts** |

### Group 3 — Executable structure (compiled workflow model)

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| ExecutableNode.cs | `ExecutableNode` | 72 | a | Compiled node model; ctor enforces dict-key↔binding-name consistency, clones payload | golden `workflowExecutable.json` (node tree) | Activities.*(18), Services, Act.Scheduling(5), Activities.Runtime, **Contracts**(4), Flowchart, **Constants** |
| ExecutableActivityStructure.cs | `ExecutableActivityStructure` | 32 | a | Compiled per-node structure payload (validating ctor, clones `JsonElement`) | rides workflowExecutable wire | Publishing.Api (compiler) |
| ExecutableChildSlot.cs | `ExecutableChildSlot` | 21 | a | Traversal projection of compiled child activities under a slot | rides workflowExecutable wire | Publishing.Api (compiler) |
| ExecutableStructureReader.cs | `ExecutableStructureReader` (static) | 106 | a | **Logic-bearing**: `ReadStructure`/`ResolveSingleSlotChild`/`MatchSingleSlotChild` — structure validation + slot resolution. STAYS: consumed by 8 **contract-side Navigators** (ControlFlow ×6, Sequence, Do) | none | **6 ControlFlow + Sequence + Do Navigators** (contract-side) |
| WorkflowExecutable.cs | `WorkflowExecutable`, `WorkflowExecutableScope` (0–1) | 85 | a | Canonical runtime artifact (immutable builder: `Flatten` DFS at ctor, `NodesById` index, `WithDeleted`/`WithRestored`); also the **arch-test assembly handle** (`typeof(WorkflowExecutable).Assembly`) | **golden `workflowExecutable.json` (`scope:0`)** | Services, Publishing.Api(4), **Contracts**(4), **Persistence**, Elsa.Server, arch tests |
| WorkflowExecutableIdentity.cs | `WorkflowExecutableIdentity` | 11 | a | Pinned-identity record (pure positional); the pin on every command payload | rides every payload + workflowExecutionState wire | Services(6), Activities.Runtime, **Contracts**, Publishing.Api |
| WorkflowExecutableIdentityComparer.cs | `WorkflowExecutableIdentityComparer` (static) | 22 | a | **Logic**: `MatchesPinnedSnapshot` (Source deliberately excluded) + `Format`. STAYS: comparer belongs with its model | none | (comparer for the identity model) |
| WorkflowExecutableResumeTarget.cs | `WorkflowExecutableResumeTarget` | 9 | a | Compiled resume-target record (pure positional) | rides workflowExecutable wire | Publishing.Api (compiler) |
| WorkflowExecutableSourceReference.cs | `WorkflowExecutableSourceReference` | 8 | a | Diagnostics/migration source ref (pure positional; not loaded at runtime) | rides workflowExecutable wire | Publishing.Api |

### Group 4 — Command payloads (wire format)

Every type here is reconstructed via `[JsonConstructor]` from the `JsonElement? Payload` of a
**persisted** `RuntimeSchedulerWorkItem` (golden `schedulerWorkItem.json`). The payloads are the
serialized contract of persisted scheduler work; splitting them from their document envelope would
split a durability contract. Consuming handler deserialization site named per row.

| File | Type(s) | LoC | Class | Rationale / `[JsonConstructor]` consumer path | §E6 |
|---|---|---|---|---|---|
| RuntimeStartActivityCommandPayload.cs | `RuntimeStartActivityCommandPayload` | 34 | a | wire payload → `WorkflowStartActivitySchedulerWorkHandler.cs:116` | persisted payload shape |
| RuntimeScheduleActivityCommandPayload.cs | `RuntimeScheduleActivityCommandPayload` | 50 | a | wire payload → `WorkflowScheduleActivitySchedulerWorkHandler.cs:112` | persisted payload shape |
| RuntimeCompleteActivityCommandPayload.cs | `RuntimeCompleteActivityCommandPayload` | 79 | a | wire payload (`Enum.IsDefined`, completion-kind↔child-id rules) → `RuntimeCompleteActivityPayloadMemo.cs:41` | payload shape; references `SchedulerCompletionKind` |
| RuntimeInvokeActivityCommandPayload.cs | `RuntimeInvokeActivityCommandPayload` | 34 | a | wire payload → `WorkflowInvokeActivitySchedulerWorkHandler.cs:931` (Activities.Runtime) | persisted payload shape |
| RuntimeCreateBookmarkCommandPayload.cs | `RuntimeCreateBookmarkCommandPayload` (+ internal validation exception) | 90 | a | wire payload → `WorkflowCreateBookmarkSchedulerWorkHandler.cs:244` | payload shape; carries `RuntimeStateChange<DurableValueState>` |
| RuntimeCheckpointCommandPayload.cs | `RuntimeCheckpointCommandPayload` (+ internal validation exception) | 82 | a | wire payload → `WorkflowCheckpointSchedulerWorkHandler.cs:308` | payload shape; carries seed vars/inputs + post-commit intents |

### Group 5 — Scheduler / checkpoint / commit state

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| RuntimeSchedulerWorkItem.cs | `RuntimeSchedulerWorkItem`, `RuntimeSchedulerWorkQuery` | 75 | a | Persisted durable work-item carrying the command payload wire; `[JsonConstructor]`, clones payload | **golden `schedulerWorkItem.json` (`commandKind:3`)** | Services(26), **Contracts**(9), Activities.Runtime, Diagnostics, **Persistence** |
| SchedulerState.cs | `SchedulerState` + `ScheduledActivityWorkItem`, `SchedulerCompletionWorkItem`, `SchedulerContinuationWorkItem`, `VolatileWaitRegistration`, `RuntimeVolatileWaitPolicyRequest/Decision` + 7 enums | 456 | a | Persisted single-writer scheduler state + durable work-item variants w/ kind-specific invariants; enums (`RuntimeWaitMode`, `VolatileWait*`, `SchedulerContinuation/CompletionKind`) | **golden `schedulerState.json`**; volatile-wait enums via contract tests | Services(5), **Persistence**, **Contracts** |
| RuntimeCheckpoint.cs | `RuntimeCheckpoint`, `RuntimeCheckpointPersistenceDecision`, `RuntimeCheckpointPersistenceMode` (0–2) | 23 | a | Named runtime boundary record + persistence-mode decision | rides checkpointCommit wire; mode enum via contract test | Services(11), Activities.Runtime, Flowchart, **Contracts** |
| RuntimeCheckpointCommit.cs | `RuntimeCheckpointCommit`, `RuntimeCheckpointStateChangeSet`, `RuntimeStateChange<T>`, `RuntimePostCommitIntent`, `RuntimeWaitDependentIntentFailurePolicy` (0–4), `RuntimeStateCategory` (0–7), `RuntimeStateChangeOperation` (0–2) | 180 | a | Commit envelope + state-change aggregate w/ `WithPostCommitOutbox` builder + `ValidateStateIdMatches`; `RuntimePostCommitIntent` has `[JsonConstructor]` (persisted outbox intent) | **golden `checkpointCommit.json`** (minimal — enums guarded by round-trip contract tests, not a populated golden value) | Services(13), Activities.Runtime, Diagnostics(3), Flowchart, **Persistence**, **Contracts** |
| RuntimeCheckpointCommitResult.cs | `RuntimeCheckpointCommitStoreResult`, `RuntimeCheckpointCommitResult`, `RuntimeCheckpointCommitFailureCodes` | 75 | a / c | Store-result DTO (**Contracts**-consumed) + engine-facing result w/ `Success`/`Failure` factories; the `...Result` half is engine-only (`RuntimeCheckpointCommitter`) but StoreResult + failure-code constants are contract/persistence-facing | none | Services; StoreResult: **Contracts**, **Persistence** |
| RuntimeSchedulerDrain.cs | `RuntimeSchedulerDrainRequest`, `RuntimeSchedulerDrainResult`, `RuntimeSchedulerWorkItemResult`, `RuntimeSchedulerDrainStopReason` (0–5), `RuntimeSchedulerWorkItemResultStatus` (0–2) | 138 | a | Drain req/result w/ LINQ aggregates + `InferStopReason` + `WithAmbientServices` copy; request/result appear on the **`IWorkflowSchedulerDrainer` contract** | enums via contract test | Services(4), **Contracts**(3), Diagnostics(3) |
| RuntimeSchedulerPoisonRecord.cs | `RuntimeSchedulerPoisonRecord`, `RuntimeSchedulerPoisonDisposition` (0–1) | 74 | a | Persisted crash record w/ disposition↔retry invariants | contract test | Services(2), **Contracts** |
| RuntimePostCommitOutbox.cs | `RuntimePostCommitOutboxItem`, `RuntimePostCommitRetryPolicy`, `RuntimePostCommitOutboxQuery`, `RuntimePostCommitOutboxDeliveryResult`, `RuntimePostCommitOutboxStatus` (0–5) | 196 | a | Persisted outbox item w/ status invariants + computed `IsTerminal`; query/result/retry-policy | **golden `postCommitOutbox.json` (`status:0`)** | Services(6), **Persistence**, **Contracts** |
| RuntimePostCommitOutboxProcessContracts.cs | `RuntimePostCommitOutboxProcessRequest`, `...Result`, `...ProcessedItem` | 50 | a | Process req/result w/ computed counts; on the **`IRuntimePostCommitOutboxProcessor` contract** | none | Services(3), **Contracts** |

### Group 6 — Input binding / materialization / value binding

| File | Type(s) | LoC | Class | Rationale | §E6 | Referenced by |
|---|---|---|---|---|---|---|
| RuntimeInputBinding.cs | `RuntimeInputBinding`, `RuntimeExpressionBinding`, `RuntimeActivityOutputReference`, `RuntimeDurableValueReference`, `RuntimeReferenceValue`, `RuntimeResolvedInput`, `RuntimeInputBindingSource` (0–4) | 170 | a | Compiled input-binding declaration w/ exactly-one-payload `Validate`; part of `ExecutableNode` | **golden `workflowExecutable.json` (`source:3`)** | **Contracts**(2), **Validators**, Publishing.Api, Services, Resolvers, Activities.* |
| RuntimeInputBindingDiagnostics.cs | `RuntimeInputBindingValidationContext`, `RuntimeInputBindingDiagnostic`, `RuntimeInputBindingDiagnosticCode` (0–1) | 18 | a | Validation-context + diagnostic DTOs; consumed by **`Validators/`** (stays) | none | **Validators**, **Contracts** |
| RuntimeInputBindingResolutionContext.cs | `RuntimeInputBindingResolutionContext` | 78 | a | Per-resolution context (`Snapshot` helper); on the Resolvers + **Contracts** surface | none | Activities.Runtime(3), Services, **Contracts**(2), Resolvers |
| RuntimeMaterializedActivityInput.cs | `RuntimeMaterializedActivityInput` | 8 | a | Materialized-input tuple result DTO | none | Activities.Runtime(4), Services, **Contracts** |
| RuntimePayloadCapturePolicy.cs | `RuntimePayloadCaptureRequest`, `RuntimePayloadCaptureDecision`, `RuntimePayloadCaptureSubject` (0–7), `RuntimePayloadCaptureMode` (0–2) | 74 | a | Capture req/decision (computed `CapturesPayload`); on the **`IRuntimePayloadCapturePolicy` contract** | enums via contract test | Activities.Runtime(3), Services, **Contracts** |
| RuntimeModelMetadata.cs | `RuntimeModelMetadata` (internal static) | 9 | a | The shared `Snapshot(...)` defensive-copy helper used pervasively by the models in this directory (internal → same-assembly only) | n/a (helper) | ~30 Models files (same assembly) |

### Group 7 — Workflow-execution lifecycle & control-plane state

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| WorkflowExecutionState.cs | `WorkflowExecutionState`, `WorkflowExecutionStatus` (0–5), `WorkflowExecutionStatusExtensions` | 41 | a | Persisted continuation-state record + `IsTerminal` extension | **golden `workflowExecutionState.json` (`status:3`)** | Services(11), Runtime.Api, **Persistence**, Activities.Runtime, **Contracts** |
| WorkflowExecutionCommand.cs | `WorkflowExecutionCommand`, `WorkflowExecutionCommandKind` (**explicit** 0–14) | 33 | a | Command record; enum has explicit ordinals = frozen wire contract | **golden `schedulerWorkItem.json` + `executionCommandTransport.json`** | Services(17), Activities.Runtime, Distributed, **Contracts** |
| WorkflowExecutionCommandProcessResult.cs | `WorkflowExecutionCommandProcessResult` | 58 | a | Result DTO w/ `NoDrain` preset + `FromDrain` projection; on the actor contract surface | none | Services(2), **Contracts** |
| WorkflowExecutionActorModels.cs | `WorkflowExecutionCommandEnvelope`, `...DispatchResult`, `...DispatchOptions`, `...ActorActivationRequest`, `...ActorDescriptor`, `...ActorPassivationRequest` + 6 enums (`WorkflowExecutionActorCapabilities` **[Flags]** 1,2,4,8,16; `...DeliveryMode`; `...DispatchStatus`; `...ActivationReason`; `...ActorStatus`; `...PassivationBoundary`) | 254 | a | Command envelope (persisted/wire) + actor-provider contract DTOs; envelope on the **`IWorkflowExecutionActorProvider` contract**, consumed by Distributed | envelope rides `executionCommandTransport.json` (`deliveryMode:1`); actor enums via contract test | Services, Distributed(6), **Contracts**(5) |
| WorkflowExecutionStartDispatch.cs | `WorkflowExecutionStartDispatchRequest`, `WorkflowExecutionStartCommandPayload`, `WorkflowExecutionStartDispatchResult` | 129 | a | Start-dispatch req/result + `WorkflowExecutionStartCommandPayload` wire payload (`[JsonConstructor]`, `ToJsonValues`) → `WorkflowStartSchedulerWorkHandler.cs:74` | payload rides schedulerWorkItem wire | Services, Runtime.Api, Publishing.Api, **Contracts** |
| WorkflowHoldState.cs | `WorkflowHoldState`, `WorkflowHold`, `SchedulerPauseDecision`, `IngressPausePolicy` + 6 enums (`WorkflowHoldScope`, `WorkflowHoldStatus`, `RuntimePauseContinuationPolicy`, `RuntimePauseBoundary`, `RuntimeIngressSourceKind`, `IngressPauseBehavior`) | 467 | a | Persisted control-plane aggregate w/ heavy hold-invariant pipeline + `WorkflowHold` factories + `IngressPausePolicy.DefaultFor`; `ControlPlaneStateId` wire-key kept stable across W14 rename | **golden `controlPlaneState.json`** (empty holds — enums guarded by round-trip contract tests) | Services, **Persistence**(2), **Contracts** |
| StimulusRoutingModels.cs | `StimulusDispatchRequest`, `StimulusStartOutcome`, `StimulusResumeOutcome`, `StimulusRoutingResult`, `StimulusRoutingMode` (0–2), `StimulusStartStatus` (0–1) | 168 | a | Stimulus routing req + result DTOs w/ factories + `BuildDispatchMetadata`; on the **`IStimulusRouter` contract**; consumed by Runtime.Api + Scheduling | enums via contract test | Services, Runtime.Api, Scheduling, **Contracts**, Activities |
| RuntimePauseDecisionRequest.cs | `RuntimePauseDecisionRequest` | 55 | a | Pause-decision request record; on the **`IRuntimePauseDecisionProvider` contract** | none | Services(2), **Contracts** |
| RuntimeHistoryEvent.cs | `RuntimeHistoryEvent`, `WorkflowLifecycleHistoryEvent`, `ActivityLifecycleHistoryEvent`, `RuntimeDiagnostic`, `RuntimeDiagnosticSeverity`, `RuntimeHistoryEventCategory` (0–?) | 179 | a | Diagnostics/history event records; consumed only by sibling models + `.Core` contract tests (`RuntimeDiagnosticsHistoryIncidentContractTests`) | contract test | sibling models + contract tests |
| TriggerStimulusDescriptor.cs | `TriggerStimulusDescriptor` | 27 | a | Trigger descriptor (validating ctor); consumed by trigger-provider activities (contract-side) | none | Activities.*(4), Act.Scheduling(4), Services, **Contracts** |
| WorkflowTriggerBinding.cs | `WorkflowTriggerBinding` | 34 | a | Persisted trigger-index record + `BuildId`/`Escape` (separator-injection-safe) | **golden `workflowTriggerBinding.json`** | Services(3), **Contracts**(3), Scheduling, **Persistence** |

### Group 8 — Timers / recurring schedules / generators / recovery / resumption

| File | Type(s) | LoC | Class | Rationale | §E6 exposure | Referenced by |
|---|---|---|---|---|---|---|
| DurableTimer.cs | `DurableTimer` | 29 | a | Persisted resume-at-deadline record (pure positional) | **golden `durableTimer.json`** | Services, **Contracts**, Scheduling, **Persistence**, **Constants**, Act.Scheduling |
| RecurringScheduleDescriptor.cs | `RecurringScheduleDescriptor` | 35 | a | Publish-time recurring-schedule descriptor (validating ctor); consumed by scheduling activities (contract-side) | none | Act.Scheduling(4), Scheduling, **Contracts** |
| RecurringScheduleKind.cs | `RecurringScheduleKind` (0–1) | 16 | a | Enum; file's own doc states member order is the wire contract | **golden `recurringTriggerSchedule.json` (`kind:0`)** | Act.Scheduling, Scheduling |
| RecurringTriggerSchedule.cs | `RecurringTriggerSchedule` | 62 | a | Persisted recurring-start schedule (CAS `NextOccurrence`) + `BuildId`/`Escape` | **golden `recurringTriggerSchedule.json`** | Scheduling(3), Services, **Persistence**, **Contracts** |
| GeneratorModels.cs | `GeneratorRegistration`, `GeneratedEvent`, `SchedulerGeneratedEventWorkItem`, `GeneratorStatus` (0–5), `GeneratorStopPolicy` (0–5), `GeneratorBackpressurePolicy` (0–3), `GeneratedEventDurability` (0–2) | 184 | a | Persisted generator registration/event/work-item w/ cross-field invariants | `GeneratorRegistration` persisted; enums via `RuntimeGeneratorContractTests` | Services(4), **Persistence** |
| RuntimeGeneratorEmissionScheduleModels.cs | `RuntimeGeneratorEmissionScheduleRequest`, `...Result` | 50 | a | Emission req/result w/ cross-consistency validation; on the **`IRuntimeGeneratorEmissionScheduler` contract** | none | Services, **Contracts** |
| RuntimeRecovery.cs | `RuntimeRecoveryScanRequest`, `RuntimeRecoveryCandidate`, `RuntimeDomainRetryRequest`, `RuntimeDomainRetryDecision`, `RuntimeDomainRetryMode` (0–3) | 136 | a | Recovery/retry req/candidate/decision w/ invariants; on the **`IRuntimeRecoveryScanner`/`IRuntimeDomainRetryPolicy` contracts** | enum via contract test | Services, **Contracts** |
| RuntimeResumption.cs | `RuntimeResumptionSweepRequest`, `RuntimeResumptionSweepResult`, `RuntimeResumptionDispatch`, `RuntimeResumptionDispatchOutcome` (0–4) | 117 | a / c | Sweep req/result on the **`IRuntimeResumptionService` contract** (stays); `RuntimeResumptionDispatch` (inner dispatch record) is NOT contract-exposed — engine-only | enum via `RuntimeResumptionPumpTaskTests` | Services, Resumption; Dispatch: Services-only |
| ExecutionLivenessState.cs | `ExecutionLivenessState`, `RuntimeExecutionLease`, `RuntimeHeartbeat`, `RuntimeDrainState`, `InterruptedExecutionState`, `RuntimeDrainMode` (0–2), `RuntimeDrainStatus` (0–4), `RuntimeInterruptionReason` (0–4), `RuntimeInterruptionStatus` (0–3) | 226 | a | Persisted operational-coordination state w/ validating ctors, `IsExpired`, `StopsNewWork` | **golden `operationalState.json`**; interruption/drain enums via contract tests | Services(5), **Persistence**(3), **Contracts** |

### Group 9 — Pipeline contract types (charter-central to ADR 0029)

| File | Type(s) | LoC | Class | Rationale | §E6 | Referenced by |
|---|---|---|---|---|---|---|
| RuntimePipelinePlan.cs | `RuntimePipelineSlotDefinition`, `RuntimePipelineMiddlewareRegistration`, `RuntimePipelinePlan`, `RuntimePipelinePlanStep`, `RuntimePipelineKind` (0–1) | 33 | a | The pipeline-plan/slot DTOs ADR 0033 explicitly names as staying | n/a | Services, **Builders**, **Constants**, **Contracts** |
| RuntimePipelineContexts.cs | `WorkflowRuntimePipelineContext`, `ActivityRuntimePipelineContext` | 46 | a | Pipeline-context records implementing **`IRuntimePipelineContext`** (Contracts); hold `Workspace` | n/a | **Middleware**(3), Services, **Contracts**(2) |
| RuntimePipelineWorkspace.cs | `RuntimePipelineWorkspace` | 72 | c → a | Mutable per-dispatch working state (checkpoint-commit staging) — genuinely engine working-state, BUT it is the type of **`IRuntimePipelineContext.Workspace`** (a Contract that stays). STAYS: moving it breaks the contract interface | n/a | **`IRuntimePipelineContext` (Contracts)**, RuntimePipelineContexts |

### Group 10 — Options / scope / engine plumbing (the genuine (c) cluster)

| File | Type(s) | LoC | Class | Rationale | §E6 | Referenced by |
|---|---|---|---|---|---|---|
| RuntimeExecutionOwnershipOptions.cs | `RuntimeExecutionOwnershipOptions` | 21 | c | DI options record; consumed only by composition root (moves) + `RuntimeExecutionOwnershipService` | none | Extensions (engine), Services |
| WorkflowDrainOrchestratorOptions.cs | `WorkflowDrainOrchestratorOptions` | 24 | c | DI options record (range-validated); composition root + `WorkflowDrainOrchestrator` only | none | Extensions (engine), Services |
| RuntimeFaultInfo.cs | `RuntimeFaultInfo`, `RuntimeFaultCaptureOptions` | 27 | a / c | `RuntimeFaultInfo` (w/ `ToSummaryString`) is embedded in persisted incident/history state; `RuntimeFaultCaptureOptions` is engine-only policy options | FaultInfo rides incident wire | Services, **Contracts**; Options: Services-only |
| LoopIterationScopeRequest.cs | `LoopIterationScopeRequest` | 34 | c | Per-iteration loop-scope request; produced+consumed only by `RuntimeContainerScopeService`/`RuntimeLoopIterationScopeFactory` | none | Services(2) only |
| RuntimeContainerScopeLayer.cs | `RuntimeContainerScopeLayer` | 21 | c | Visible-scope-chain layer; `RuntimeVariableScopeFactory`/`RuntimeContainerScopeService` only | none | Services(2) only |
| RuntimeScopedVariableValue.cs | `RuntimeScopedVariableValue` | 10 | c | The single cleanest move candidate: `(Name, ReferenceKey, object? Value)` — `object?` value = not JSON-durable; returned from one engine method (`RuntimeContainerScopeService.ReadScopeVariableValues`); no contract iface, no persistence, no test | none | Services(1) only |
| RuntimeChildActivityScheduleRequest.cs | `RuntimeChildActivityScheduleRequest` | 29 | a | Child-schedule request; on the scheduling contract surface | none | Activities.Runtime(2), Services, **Contracts** |

### Group 11 — Remaining files (all (a), verified)

| File | Type(s) | LoC | Class | Rationale | §E6 | Referenced by |
|---|---|---|---|---|---|---|
| RuntimeWaitRegistration.cs | `RuntimeWaitRegistration`, `RuntimeWaitRegistrationStatus`, `RuntimeEarlySignalPolicy` | 112 | a | Wait-intent registration record; consumed by sibling models + `.Core` wait-intent contract tests | contract test (`RuntimeWaitIntentContractTests`) | sibling models + contract tests |

## Why the (c) cluster stays

1. **`RuntimePipelineWorkspace`** — typed on the `IRuntimePipelineContext.Workspace` contract
   property. Moving it breaks a `.Core` contract. Hard STAY.
2. **The options + scope + dispatch records** — each consumed only by `Services/`/`Extensions/`
   code that itself moves. Moving these is *possible* but yields zero charter benefit: they are
   tiny (≤34 LoC, ~140 LoC total), carry no domain-decisive logic, and their removal would not
   change `.Core`'s "honest contracts/models" character. The ADR's refactor-cost test (§2.16) and
   MD-6's semantic framing both target the removal of the **engine** (`Services/`, 10k LoC of
   decision logic), not every small record a service touches.
3. **Uniformity** — these records share the `RuntimeModelMetadata.Snapshot` helper and the record
   idioms of their durable siblings; they are cohesive with the model layer they live in.

## Final ratified recommendation

**Move nothing out of `Models/`. All 77 files stay in `Elsa.Workflows.Runtime.Core`.** Every file
falls under at least one anchor:

- **Anchor A — durable/wire format (~46 files):** persisted state records + their ordinal-encoded
  enums (pinned by 16 groundwork + 2 distributed golden fixtures) and the `[JsonConstructor]`
  command payloads deserialized from the persisted `RuntimeSchedulerWorkItem.Payload`.
- **Anchor B — contract-interface surface (~22 files):** req/result/context types appearing on
  `.Core` `Contracts/`, `Middleware/`, `Builders/`, or `Validators/` signatures, and/or consumed by
  contract-side projects (the 8 activity Navigators via `ExecutableStructureReader`, the
  Publishing.Api compiler, Persistence.Groundwork).
- **Anchor C — cohesion/no-benefit (the 9-type (c) cluster + `RuntimeModelMetadata`):**
  engine-adjacent but tiny, logic-free, or shared-internal; moving them fragments the model layer
  for no charter gain.

**Carried into the Stage-2 guardrail:** the semantic guard test must key on type-name/role
suffixes and namespace, NOT on "has methods" — ~55 behavioral models legitimately stay in `.Core`.
