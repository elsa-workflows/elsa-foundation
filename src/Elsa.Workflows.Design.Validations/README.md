# `Elsa.Workflows.Design.Validations`

Baseline universal validators for the workflow-design sub-domain. Subscribes to
`OnDraftValidating` (from `Elsa.Workflows.Design.Validations.Core`) and contributes
`ValidationError` entries that the mutation pipeline persists to the
`WorkflowDefinitionDraftValidation` sibling per Unit C FR-023 / FR-027.

Per framework §2.22 — this README documents what the feature registers.

---

## Activation

`WorkflowDesignValidationsFeature : IShellFeature` — named `WorkflowDesignValidations`.

## Settings

`WorkflowDesignValidatorOptions` (bound via the feature's `MaxRecursionDepth` property; default `100`).

- `MaxRecursionDepth` — safety net for the iterative activity-tree walker. Validators that
  recurse into `ActivityNode.ChildActivities` (required-input/output, variable-expression
  resolver) stop descending past this depth. Iterative DFS internally, so the .NET call stack
  is never the actual risk — the bound guards against cyclic / malformed Draft data.

## Domain event handlers registered

All five handle `IDomainEventHandler<OnDraftValidating>`. Errors flow back via
`event.AddValidationError(...)`; the pipeline reads `event.Errors` after the chain completes
and upserts into the validation sibling in the same transaction as the Draft's state.

| Validator | Scope | `(Path, Type)` emitted |
|---|---|---|
| `OrphanActivityValidator` | Root-level (workflow graph) | `{NodeId}` · `Graph/OrphanActivity` |
| `StartActivityValidator` | Root-level (workflow graph) | `$workflow` · `Graph/StartActivity` |
| `VariableUniquenessValidator` | Workflow-scope | `$workflow/variables/{Name}` · `Variables/Uniqueness` |
| `RequiredInputOutputValidator` | Root + nested (recurses) | `{NodeId}/inputs|outputs/{ReferenceKey}` · `InputOutput/MissingRequired` |
| `VariableExpressionResolverValidator` | Root + nested (recurses) | `{NodeId}/inputs|outputs/{ReferenceKey}` · `Expressions/UnresolvedVariable` |

## Tasks registered

None (no startup / recurring / scheduled tasks).

## Notes

- **Variable lookup is by `ReferenceKey`, not `Name`** — the id is stable across renames; the
  name is mutable. The variable-expression validator compares
  `ArgumentValue.Value` (the serialised variable id) against `VariableDefinition.ReferenceKey`.
- **Workflow-level required-input/output check** is deliberately a no-op in Unit C. The design
  surface for `WorkflowDefinitionState.Inputs` / `Outputs` carries the `IsRequired` flag (per
  FR-036) but no default value or internal binding to validate against; the actionable
  workflow-level semantic is downstream (Unit D / E). Activity-level coverage is complete.
- **Activity-specific validators** ship in their owning activity feature per FR-034 (e.g.
  `Elsa.Http.Activities.Design` ships HTTP validators), NOT here.

See [`../Elsa.Workflows.Design.Validations.Core/EVENTS.md`](../Elsa.Workflows.Design.Validations.Core/EVENTS.md)
for the `OnDraftValidating` / `OnDraftValidated` event surface.
