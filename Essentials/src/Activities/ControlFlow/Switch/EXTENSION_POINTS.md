# Elsa.Activities.Switch Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `Switch.Case[{match}]` child slots (one per case, at most one branch activity each)
- `Switch.Default` child slot (at most one branch activity)
- A composite outcome per case match value, plus a `Default` outcome for the no-match branch

## Cross-domain contributions

- `SwitchStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the per-case and default branch slots from the authored structure, compiles the executable
  structure (the ordered cases with their match values and branch node ids, plus the default branch node
  id), and round-trips both through publishing so the runtime resolves the matching branch without
  re-reading the design document. `Switch` is not a container scope, so it declares no container-scoped
  variables (`SupportsScopedVariables` defaults to `false`).
- `SwitchDuplicateCaseValidator` implements `IDraftValidator`
  (`Elsa.Workflows.Design.Validations.Core`). It walks the Draft activity tree and emits a
  `Switch/DuplicateCase` `ValidationError` for any `Switch` node that declares the same case match value
  more than once. This is the design-time, author-facing surface; it does not block saving the Draft, but
  the promotion gate blocks publishing a Draft that carries the error. The compile-time guard in
  `SwitchStructureHandler.CompileExecutableStructure` and the runtime guard in `SwitchNavigator` are
  backstops; all three share the ordinal duplicate rule in `SwitchCaseRules`.

## Case conditions (4.1 / schema 2.0.0)

The v2 handler projects each present `condition` container as a normal Boolean input at `Case:{caseId}:Condition`, including unbound containers. `caseId` is stable and unique; `match` remains the human label and outcome. Value is consumed from ordinary inputs and projected only if a legacy case remains. Child replacement retains arguments, identities and unknown payload fields. The v1 handler remains registered for existing definitions/artifacts.

`SwitchStructureInputPolicy` contributes `IRuntimeStructureInputMaterializationPolicy` for this kind/schema. It reads conditions in case order through the constrained normal materializer, stopping at the first true condition or legacy match. Switch implements `IRuntimeStructureInputConsumer` to receive pinned activation values. These contracts are cataloged in [Workflows.Runtime](../../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#iruntimestructureinputmaterializationpolicy-core--elsaworkflowsruntimecore) and [Workflows.Design](../../../Workflows/Design/Api/EXTENSION_POINTS.md#iactivitystructurehandlerprojectinputs-core--elsaworkflowsdesigncore).
