# Contract: DispatchWorkflow Fire-and-Forget

## Activity surface

Logical type: `DispatchWorkflow` (Foundation-native; no Elsa Core alias).

Inputs:

- `WorkflowDefinitionId: string` — static dropdown backed by `DispatchWorkflow.WorkflowDefinitions` options.
- `Inputs: IReadOnlyDictionary<string, object?>` — declared workflow input values only.
- `WaitForCompletion: bool = false`.
- `CancelChildOnParentCancellation: bool = true`; ignored in detached mode.
- `CorrelationId: string?`; blank/null inherits.

Outputs:

- `ChildWorkflowExecutionId: string` after the dispatch checkpoint commits.
- `Result: DispatchWorkflowResult?`, reserved for waited terminal behavior. Its stable shape carries the child execution ID, terminal `WorkflowDispatchStatus`, JSON-safe output entries with declared type/redaction state, and safe diagnostic metadata. #676 leaves it unset; #679 populates it.

Outcomes: `Dispatched`, `Completed`, `Faulted`, `Cancelled`, `DispatchFailed`. #676 emits only `Dispatched`.

## Authoring options

The provider returns a definition only when the current tenant-scoped read can see the active definition and exactly one live Published source reference identifies its current executable. Zero or ambiguous live sources produce no option. Labels use the definition name with stable definition ID as value.

## Publication pin

`DispatchPinSource` accepts only a literal nonblank `WorkflowDefinitionId`. Publishing invokes it through the named `OnExecutableNodeMetadataCollecting` fan-in event and the single `CollectExecutableNodeMetadata` handler. The source resolves one live Published source and records the full executable/source identity in compiled node metadata. Runtime reads only this pin.

## Parent checkpoint

The activity requests one mandatory completion checkpoint. The commit contains:

1. completed activity state with `Dispatched`;
2. durable `ChildWorkflowExecutionId` output;
3. Pending workflow-dispatch record upsert;
4. child-start post-commit intent;
5. ordinary parent completion-propagation scheduler intent.

If the commit fails, none of these become visible. Child start never runs inline.

## Child-start delivery

The `Elsa.Activities.DispatchWorkflow.StartChild` handler calls `IWorkflowStartDispatcher.DispatchAsync` with:

- the pinned artifact ID and exact pinned source-reference selection;
- `RequireLiveReference` for #676;
- the reserved child execution ID and stable idempotency key;
- authored values only in `Inputs`;
- explicit parent linkage, inherited/overridden correlation, tenant, partition, run kind, and authority snapshot.

The dispatcher selects the configured actor provider. Duplicate delivery reuses the child identity and idempotency key.

## Durability statement

The in-memory composition is asynchronous and semantically idempotent within one process, but it is not process-crash durable. Groundwork dispatch-state persistence and restart convergence are #678.
