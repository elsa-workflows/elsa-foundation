# Integration decisions for #2898

Source baseline: `d3ebb980e2dbebeed680808e58c1edd694fae0f7`; specification reviewed at `10af302d89ba10d3c91d8064d6c3c14b1123cf9c`. These are bounded implementation decisions, not renewed architecture research or runtime qualification.

## 1. Extend the existing authority

**Decision**: Add explicit model material/state and dispatch participation to Workflows.Runtime. Retain the existing in-process mailbox, partition-bound scopes, ownership service, scheduler queue/claims, checkpoint transaction, outbox and receipt marker. Extract only the shared ownership/dispatch mechanics needed by native and direct BPMN consumers.

**Source**: [scoped command executor](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/ScopedWorkflowExecutionCommandExecutor.cs), [drain orchestrator](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowDrainOrchestrator.cs), [checkpoint store](../../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs).

**Rationale**: Scope and execution fence already join native participants, consumed scheduler claims, outbox and replay marker. Reusing that atomic decision avoids a second coordinator ledger or cross-store atomicity claim. Model-bearing checkpoints are mandatory durable boundaries initially; existing native coalescing remains intact.

**Rejected**: Separate SQLite runtime; new actor framework; treating local serialization as a durable fence; storing model state in string metadata; a universal graph or BPMN name-switch in the shared runtime.

## 2. Give direct execution real material and state

**Decision**: Preserve `WorkflowExecutable` and all native constructors. Add a versioned opaque model-material variant through an additive runtime material contract implemented by the existing runtime artifact provider. Share artifact identity, physical incarnation/coordination, retained dependency closure and deletion guards. Add model state as an optional versioned checkpoint participant, with dedicated typed storage and accepted-input receipts in the same transaction.

**Source**: [WorkflowExecutable](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutable.cs) requires/enumerates `RootActivity`; [root write lease](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableRootWriteLeaseManager.cs) validates complete pins/closure; [state changes](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs) are native-typed; [fingerprint](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommitFingerprint.cs) explicitly preserves historical shapes.

**Rationale**: A fake native root or synthetic activity context would fail direct-host acceptance. An opaque payload interpreted by its model preserves separate semantics. New material is distinguishable from the historical native DTO; native readers/writers keep their bytes. Model state and input receipt cannot be accepted separately from owed work.

**Rejected**: Making native `RootActivity` silently nullable; reusing a string-only root lease without closure validation; metadata JSON masquerading as model state; acknowledging unknown protocols through the current Noop scheduler fallback.

## 3. Share evaluation, keep host mappings explicit

**Decision**: Extract an internal BPMN evaluation component taking explicit graph/state/live-work/input values and returning the released interpreter's state/continuation/commands. Existing `BpmnProcessRuntime` remains the legacy native adapter. The direct driver and a new native `RunBpmn` invocation adapter use the same direct-model lifecycle; the old adapter also consumes the shared evaluation component without changing saved-state contracts.

**Source**: [BpmnProcessRuntime](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnProcessRuntime.cs), [BpmnWorkLedger](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnWorkLedger.cs), [BpmnStateCodec](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnStateCodec.cs).

**Rationale**: The ledger's exact native parent/element/token/iteration/supersession checks are native concerns. Direct work must have real model-owned handles and attempts. Both direct and new embedded invocation use one model execution path; historical embedded state remains executable on its compatible path. No second interpreter is introduced.

**Rejected**: Synthetic `IRuntimeActivityExecutionContext`; rewriting all active native BPMN instances; shrinking the established embedded profile to the new direct subset; copying token-routing rules into the host.

## 4. Use a real native ETL service worker

**Decision**: The initial service binding starts an exactly pinned published native workflow containing `RunEtl`, with a deterministic work identity and durable typed result address to the direct model. The native workflow is a service worker; the BPMN coordinator has no enclosing native workflow. Add the model/native bridge through shared checkpoint/outbox contribution contracts, without manufacturing a native parent for `DispatchWorkflow`.

**Source**: [RunEtl](../../Extensions/Etl/src/Workflows/Runtime/Activities/RunEtl.cs), [native ETL delivery](../../Extensions/Etl/src/Workflows/Runtime/Services/EtlNativeResultDelivery.cs), [cancellation enricher](../../Extensions/Etl/src/Workflows/Runtime/Services/EtlCheckpointEnricher.cs), [DispatchWorkflow](../../Essentials/src/Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs), [ETL fingerprint](../../Extensions/Etl/src/Core/Runtime/EtlRuntimeIdentity.cs).

**Rationale**: Existing ETL requires a real committed native bookmark. Its admission/cancellation tombstone and terminal-delivery proofs already work. `BusinessError` is a completed activity outcome, so workflow status alone is insufficient: the service contract must export the typed `RunEtl` result. Parent interruption reaches real native cancellation and the ETL cancel intent; inspect the resulting ETL disposition after restart.

**Rejected**: Dummy `EtlNativeResultAddress`; a direct bridge that cancels only BPMN; widening ETL invocation/lease wire to a new address family in this first task; a second ETL algorithm or one workflow per record.

## 5. Keep operations narrow and honest

**Decision**: Add supported scope-authorized direct-model execution/task inspection, human completion, cancellation and incident retry commands. Reuse existing authorization and transport conventions; retain accepted command receipts and exact invocation/attempt identity. Native incident retry is not treated as a generic model port. Human completion does not imply task claiming, assignment, delegation or a complete task-management product.

**Source**: [runtime permissions](../../Essentials/src/Workflows/Runtime/Api/Authorization/WorkflowRuntimePermissions.cs), [stimulus dispatch](../../Essentials/src/Workflows/Runtime/Api/Endpoints/Stimuli/Dispatch/Endpoint.cs), [incident actions](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Incidents/IncidentResolutionActions.cs).

**Rationale**: No existing first-class human-task completion contract covers direct models. Exact authorized commands are necessary for this witness; full #2907 operations/Studio remain separate. Existing first-delivery broadcast snapshots remain authoritative and must cover model recipients through an explicit qualified extension, not a native bookmark fiction.

## 6. Preserve domains and deployment shapes

**Decision**: Keep generic coordination contracts in existing Workflows.Runtime.Core; BPMN contracts in a small Activities.Bpmn.Core sibling with real host/API/adapter consumers; BPMN interpreter/driver implementations in the existing BPMN implementation family. Add an Activities.Bpmn.Api consumer for direct commands. Do not add a new root domain or Foundation.Core. Runtime paths consume immutable material only. Existing importer/Studio authoring remain native-compatible and under their current ownership.

**Rationale**: This follows the current domain tree and keeps heavy Bpmn libraries out of `.Core`. Public surfaces exist only for actual host/model/provider authors; implementations remain internal. The narrow material/state/dispatch contracts are integration surfaces, not a claim that all existing ETL/Rules stores already use a universal kernel.

## 7. Qualification boundaries

**Decision**: Actual direct/embedded process and upgrade journeys use the built-in host with SQLite runtime/ETL stores. New common material/state provider logic and additive migrations must be exercised on all affected first-party EF engines before their support is asserted. Unsupported/decorated providers default-refuse model capability while preserving native behavior. No SQLite result qualifies multi-node placement or other actor providers.

Actual pre-writer captures precede affected source edits. Historical #2735 evidence remains unchanged. Full-library conformance, generic task administration, direct-host Studio product UX, continuous/SQL/RabbitMQ ETL, Orleans, arbitrary alteration/migration and the task-runner follow-up are outside this task's claimed completion.
