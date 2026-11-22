# Data model: Step workflow state

## Existing authoritative state

| State | Authority | Projection |
|---|---|---|
| Correlation | `WorkflowExecutionState.CorrelationId` | Existing `identity:*` durable slot used by expression materialization |
| Instance name | `WorkflowExecutionState.SystemMetadata[RuntimeMetadataKeys.InstanceName]` | Existing identity durable slot and instance views |
| Named workflow output | `DurableValueState`, `output:<name>` / `durable-output:<name>` | Existing workflow-output projection/API, preserving policy/type |
| Variable | Existing root/container frame and S-7 session | Existing versions/read bases/write history; unchanged |
| Wait correlation | Bookmark/registration metadata from that wait's authored input | Existing stimulus correlation index; never rewritten here |

No new persistent aggregate or value-history table is introduced. Keep persisted keys/discriminators stable.

## New transient session

One `StepWorkflowState` is owned by one inline leaf evaluation and references its existing workflow execution, invocation and attempt IDs. It has a snapshot of readable working output/identity state, an ordered last-assignment map, a sticky refusal and protection state. It must never survive into a resumed evaluation or be serialized as a live capability.

Each intent has an address kind (Output, Correlation, InstanceName), an output name for Output only, a JSON value with explicit presence, type/policy information needed by the existing durable encoder, and evaluation identity. Output names use ordinal equality; identity kinds are singletons. User-shaped strings such as `__proto__` are data keys, not object properties that can acquire authority. The last explicit write wins at one address within one evaluation; distinct addresses cannot overwrite each other during dictionary merging.

A new or replacement body output uses the `Any` type descriptor and InstanceInline owner policy, strengthened by source protection with the existing combiner. It replaces the payload/type, with prior destination policy/storage retained as minima and a fresh encoded external reference where required; an old type is not a declared schema. Reading the old payload additionally contributes source protection minima. Policy conflicts or unsupported encryption/storage refuse the evaluation rather than silently discarding protection.

Missing output reads return absent; null output reads return present JSON null. Assignment of missing/undefined is refused. Identity null is a clear, not absence of an intent; use a separate written flag so clearing a value is not dropped when merging records.

## Preparation/result

Prepared changes carry a combined workflow state (root frame plus both identity changes), existing durable-value changes for outputs/identity, invocation attribution and the existing variable/capture changes. No store mutates while preparing. A sticky refusal or any later validation/encoding/capture failure returns a failed preparation and no successful checkpoint changes.

Fold duplicate physical state IDs deliberately, then let captures replace earlier writes at actual overlapping addresses. Never rely on timestamp ties or accidental list ordering. Same display names in `variable:*`, `graph-variable:*` and `output:*` are distinct.

## State transitions

```text
created -> body executing -> validated -> checkpoint accepted -> discarded session
                    \-> refused/fault/cancel -> fault/cancel checkpoint only -> discarded session
```

A successful suspension accepts that evaluation, then discards its session. A resume/retry creates a new session from current working state. Durability and redelivery follow the existing checkpoint marker/persistence policy; no setter changes that boundary.

## Inspection

Add value-free effect kinds/known target names to the node's pinned authoring/runtime metadata and accepted invocation inspection. Do not include a computed output name derived from protected data. Payloads remain in existing output/capture-policy channels. Refused evaluations carry stable code/reason but no committed-state receipt. Recovery may retain an accepted completion only if it can reconstruct every effect; otherwise recorded-completion acceptance is disabled for that completion.
