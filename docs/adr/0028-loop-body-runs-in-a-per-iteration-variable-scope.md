# Loop Body Runs In A Per-Iteration Variable Scope

Status: proposed

A loop activity (`ForEach`, `For`, `While`, `Do`; #264–#267) runs its body once per pass inside a dedicated **iteration scope** — a variable scope that declares the loop's current-item variable (and, where the loop offers one, a zero-based iteration index) and that is isolated per pass so two passes never observe each other's item. This decision settles that shared primitive (#259) ahead of the loop activities themselves; the activities depend on it rather than each re-inventing iteration-variable handling.

The iteration scope reuses the container-scoped variable machinery of [ADR 0027](0027-scoped-variable-references-include-declaring-scope.md) rather than introducing a parallel one. A loop's iteration variable is a scoped variable whose **declaring scope identity is the loop activity's node identity** and whose **runtime values are scoped to one concrete iteration**, exactly as ADR 0027 scopes container variable values to one concrete container activity execution. The loop owner is the only new piece; the resolution, name-based access, shadowing, read/assign visibility, and chain layering rules are unchanged from ADR 0027.

Iterations are isolated by the engine **iteration identity** (`IterationId`), not by the reference key. Every pass of one loop reuses the same item (and index) reference keys — the authored declaration is stable across passes — while each pass is a distinct iteration with its own value store. Concretely, the runtime iteration scope carries the loop's node id as its declaring scope identity and the pass's `IterationId` as its execution identity, so pass *N* and pass *N+1* are distinct scopes and one pass's current item can neither overwrite nor be read as another's. This is the same per-execution isolation ADR 0027 already specifies, keyed on iteration instead of container execution.

The iteration scope is the **innermost** layer of the body's visible scope chain. It is layered on top of the scope chain the runtime already threads into the loop activity's own execution — the loop's enclosing container and workflow variables. The body therefore resolves its current item, its index, and every enclosing container/workflow variable through one chain. Assignments the body makes to enclosing container or workflow variables flow to their owning scopes through the unchanged ADR 0027 chain and are observed by later passes and sibling activities; the iteration layer only adds the current item and index.

The loop owner publishes a distinct `IterationId` per pass and threads that same identity through the body child's scheduling provenance (`ActivitySchedulingProvenance.IterationId`), so the body activity's `ActivityExecutionState.IterationId` and its iteration scope agree. This mirrors how the Flowchart engine already threads an iteration key through provenance for loop-back routing; loop activities adopt the same engine concept rather than a private one.

The current item and index are loop-owned per-pass state. Assigning a new current item or advancing the index is the loop owner's responsibility on the next pass, not the body's; the body reads them. Whether a loop also offers an authored, body-writable iteration variable beyond the read current item is left to the individual loop-activity decisions (#264–#267) and is out of scope here.

Iteration-variable durability follows the normal runtime checkpoint boundary, with no iteration-specific persistence path — consistent with ADR 0027. When execution resumes mid-loop, the runtime recovers the values for the original concrete iteration rather than reinitializing the pass; when a pass completes, its iteration values stop being live for later runtime expressions but may be retained as inspection or history evidence under the workflow execution retention and redaction policy.

This decision covers the shared iteration-scope primitive only. It does not define collection enumeration, loop termination conditions, counted-loop bounds, early exit (`Break`/`Continue`), or the authoring surface for any specific loop activity; those belong to #264–#267.

## Contract for `ForEach`/`For`/`While`/`Do` (#264–#267)

The primitive is `RuntimeLoopIterationScopeFactory.BuildIterationScope(LoopIterationScopeRequest, parent)` in `Elsa.Workflows.Runtime.Core`. For each pass a loop activity:

1. Chooses a distinct `IterationId` for the pass.
2. Calls `BuildIterationScope` with a `LoopIterationScopeRequest` carrying the loop's node id (`OwnerNodeId`), that `IterationId`, the current item (`ItemReferenceKey`/`ItemName`/`Item`), and optionally the zero-based index (`IndexReferenceKey`/`IndexName`/`Index`) — passing the loop body's enclosing visible scope chain as `parent` (or `null` when the loop has no enclosing container/workflow scope).
3. Schedules the body child with that same `IterationId` in `ActivitySchedulingProvenance.IterationId`, so the body execution's iteration identity matches its scope.

The runtime then resolves the body's `currentItem`/`index` through the returned scope by reference key (declaring scope = the loop node id), and resolves enclosing variables through the chained parent. The factory validates required values (non-blank owner/iteration/item) and rejects an index that reuses the item's reference key or omits its name.
