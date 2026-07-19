# Research: Dispatch a Published Workflow Fire-and-Forget

## Decision 1: Split runtime and design assemblies

**Decision**: Create `Elsa.Activities.DispatchWorkflow.Runtime` and `Elsa.Activities.DispatchWorkflow.Design`.

**Rationale**: Runtime owns the activity, dispatch state, intent handler, and resumption dependency. Design owns options and publication pinning. This matches the documented `IActivityInputOptionsProvider` rule that runtime activity libraries declare only a stable provider key.

**Alternatives rejected**: One assembly referencing Design would violate the runtime/design direction. Putting activity-specific logic into Publishing.Api would make the generic compiler own a feature it should only host.

## Decision 2: Require one unambiguous live Published source

**Decision**: Group live Published `WorkflowExecutableSourceReference` rows by definition. Return an option and allow pinning only when exactly one accessible live source remains. Fail closed for zero or multiple sources.

**Rationale**: The authored contract selects a definition, not a slot. Publishing supports multiple named slots and `IWorkflowStartDispatcher` already rejects ambiguous live provenance. Arbitrary source selection would make publication nondeterministic.

**Alternatives rejected**: Hard-coding the default slot ignores publication policy and named-slot configurations. Adding a slot input changes the authoritative activity contract.

## Decision 3: Add generic async node-metadata sources behind a named fan-in event

**Decision**: Add `IExecutableNodeMetadataSource`, a Publishing.Core contract that receives a compiled `ExecutableNode` and compile context and returns metadata claims. `ExecutableNodeMetadataEnricher` publishes `OnExecutableNodeMetadataCollecting`; Publishing's single `CollectExecutableNodeMetadata` handler resolves every source, stamps ownership, and applies claims deterministically before hashing/assembly. Duplicate keys with unequal values fail deterministically. The feature implementation is `DispatchPinSource`.

**Rationale**: Resolving a Published child is asynchronous and activity-specific. Sources keep that work in the contributing domain, while the named event and single Publishing-owned handler preserve one fan-in topology and one conflict policy. A keyed switch on `DispatchWorkflow` in the compiler would invert ownership.

**Alternatives rejected**: Replacing the authored definition ID with an artifact ID corrupts the activity’s logical input. Loading Design state at runtime violates artifact-only execution.

## Decision 4: Pin full artifact and source identity in node metadata

**Decision**: Record artifact ID, definition/version IDs, artifact version/hash, source-reference ID, publication ID, and slot ID under stable DispatchWorkflow-owned metadata keys.

**Rationale**: Runtime must start exactly what publication approved and must have enough provenance for later retention, replacement, and inspection work.

**Alternatives rejected**: Artifact ID alone loses authoritative publication provenance. Re-resolving current publication at runtime silently mutates published behavior.

## Decision 5: First-class dispatch state in the checkpoint model

**Decision**: Add `WorkflowDispatchRecord`, `IWorkflowDispatchStore`, and workflow-dispatch changes to `RuntimeCheckpointStateChangeSet`/`RuntimeStateCategory`. The activity stages a `WorkflowDispatchCheckpointRequest`; the invoke handler folds it into the existing completion commit.

**Rationale**: A separate store write cannot be atomic with activity completion and the outbox. Encoding records as generic durable values would not provide the first-class lifecycle/query contract required by the parent PRD.

**Alternatives rejected**: Inline child start violates ADR 0020. An outbox row alone cannot distinguish lifecycle or support later inspection/cancellation/redrive.

## Decision 6: In-memory implementation plus explicit Groundwork rejection

**Decision**: #676 implements in-memory atomic dispatch projection. Groundwork rejects non-empty dispatch state changes with a capability diagnostic until #678 implements provider-backed persistence and crash convergence.

**Rationale**: #676 explicitly proves asynchronous in-memory semantics without process-crash durability; #678 owns Groundwork restart durability. Silent omission is never acceptable.

**Alternatives rejected**: Claiming Groundwork support without atomic projection. Pulling all #678 persistence/inspection work into this tracer bullet.

## Decision 7: Versioned deterministic dispatch identity

**Decision**: SHA-256 hash a canonical length-prefixed tuple of parent workflow execution ID and parent activity execution ID. Use a versioned textual prefix and derive dispatch record ID, child execution ID, intent ID, and idempotency key from it.

**Rationale**: Same activity execution must converge across replay, while loop/business-retry activity executions remain distinct. Random generators cannot provide that contract.

**Alternatives rejected**: String concatenation without canonical framing can be ambiguous. Random IDs require an already-persisted lookup before the first atomic commit.

## Decision 8: Typed lineage and authority plumbing

**Decision**: Add an immutable `WorkflowExecutionAuthoritySnapshot` to start request/payload/state, with system identity and root initiator. Start plumbing also explicitly carries parent execution ID, tenant, correlation, partition, and run kind. For a child, system identity represents the parent workflow execution and root initiator is inherited.

**Rationale**: The platform has tenant/partition models and `RequestedBy`, but no typed execution-authority/root-initiator contract. Workflow inputs and loose metadata are spoofable and insufficient for an inheritance invariant.

**Alternatives rejected**: Relying on ambient partition during later global delivery can select the pump’s scope rather than the parent’s. Treating `RequestedBy` as both active system identity and provenance loses the distinction required by the PRD.

## Decision 9: Existing dispatcher and actor provider remain authoritative

**Decision**: The contributed child-start handler builds `WorkflowExecutionStartDispatchRequest` with the reserved child ID, exact source selection, stable idempotency, inputs, and inherited context, then calls `IWorkflowStartDispatcher.DispatchAsync`.

**Rationale**: `WorkflowStartDispatcher` already gates source provenance, builds the Start envelope, and selects `IWorkflowExecutionActorProvider`, preserving in-process and future distributed behavior.

**Alternatives rejected**: Direct actor lookup in the activity or handler duplicates source gating and start-envelope construction. A new transport abstraction is out of scope.

## Decision 10: Explicitly reject wait mode in #676

**Decision**: `WaitForCompletion=true` throws an actionable unsupported-slice error before staging dispatch.

**Rationale**: The stable input must exist now, but success/result/resume is #679. Silently executing detached semantics would violate author intent.

**Alternatives rejected**: Hiding the property until #679 breaks the agreed stable activity contract. Creating an incomplete bookmark risks a permanent wait.
