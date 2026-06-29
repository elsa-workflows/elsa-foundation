# Elsa.Activities.For Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `For.Body` child slot (at most one body activity)
- A per-iteration `index` scoped variable (#259 / ADR 0028), exposed to the body each pass
- A `Done` composite outcome when the range is exhausted or empty
- A recognized `Break` body outcome (#261) that ends the loop early, matched by name

## Cross-domain contributions

- `ForStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It projects
  the single body slot from the authored structure, compiles the executable structure (the body node id),
  and round-trips both through publishing so the runtime resolves the body without re-reading the design
  document. `For` is not a container scope, so it declares no container-scoped variables
  (`SupportsScopedVariables` defaults to `false`); the per-iteration `index` is supplied at runtime by the
  loop owner via `RuntimeLoopIterationScopeFactory`, not declared as a container-scoped variable.

## Runtime extensions consumed

- `ActivityChildCompletedContext.CompletedChildIterationId` (`Elsa.Activities.Runtime.Core`) surfaces the
  completed child's engine `IterationId` to composite child-completion handlers. `For` reads it to recover
  the just-completed index across the runtime's stateless re-construction of the composite. This is a
  shared hook the remaining loop activities (`ForEach`/`While`/`Do`, #264/#266/#267) also build on.

- `RuntimeMetadataKeys.LoopIteration*` + `RuntimeContainerScopeService` (`Elsa.Workflows.Runtime.Core`)
  wire #259's `RuntimeLoopIterationScopeFactory` into the real execution path. A loop owner writes the
  per-pass iteration variable (owner node id, item/index name, JSON value) into the body child's
  scheduling-provenance metadata; `RuntimeContainerScopeService.BuildScopeAsync` reads it and layers the
  innermost per-iteration scope onto the body's container chain, so the body resolves the current index
  through the registered `IExpressionEvaluator`. This is the generic loop-variable threading the other
  loop activities reuse. (The iteration scope is loop-owned, read-only per pass: it carries the engine
  `IterationId` as its execution id, has no backing container execution, and so is skipped by
  `PersistScopeMutationsAsync` — body assignments still flow to enclosing container/workflow scopes via
  the unchanged ADR 0027 chain.)
