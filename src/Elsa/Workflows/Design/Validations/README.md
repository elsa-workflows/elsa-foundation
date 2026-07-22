# `Elsa.Workflows.Design.Validations`

Baseline universal validators for the workflow-design sub-domain. Implements the
`IDraftValidator` contributor interface (from `Elsa.Workflows.Design.Validations.Core`)
and **returns** `ValidationError` entries. The single `ExecuteValidations` handler
(also registered here) aggregates every validator's returned errors onto the
`OnDraftValidating` event's `Errors` collection; the publishing command reads them back and
surfaces them on `OnDraftValidated` (create/update) or uses them as the promotion gate (FR-024).
Errors are derived state — recomputed on every mutation and re-derived on demand — and are not
persisted.

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

## Contributor interfaces registered

All of the following implement `IDraftValidator` and are registered via DI
(`services.AddScoped<IDraftValidator, X>()`). Each **returns** its `ValidationError` set from
`Validate(...)`; the single `ExecuteValidations : IEventHandler<OnDraftValidating>` handler
(registered here) resolves `IEnumerable<IDraftValidator>`, runs each, and adds every returned
error to `event.Errors`. The publishing command reads `event.Errors` back after dispatch and
surfaces them on `OnDraftValidated` / uses them as the promotion gate; the errors are not
persisted.

| Validator | Scope | `(Path, Type)` emitted |
|---|---|---|
| `UnknownActivityVersionValidator` | Root + nested (recurses) | `{NodeId}` · `Graph/UnknownActivityVersion` |
| `UnhandledActivityStructureValidator` | Root + nested (recurses) | `{NodeId}` · `Graph/UnhandledActivityStructure` |
| `StartActivityValidator` | Root-level | `$workflow` · `RootActivity/Missing` |
| `VariableUniquenessValidator` | Workflow-scope | `$workflow/variables/{Name}` · `Variables/Uniqueness` |
| `RequiredInputOutputValidator` | Root + nested (recurses) | `{NodeId}/inputs|outputs/{ReferenceKey}` · `InputOutput/MissingRequired` |
| `VariableExpressionResolverValidator` | Root + nested (recurses) | `{NodeId}/inputs|outputs/{ReferenceKey}` · `Expressions/UnresolvedVariable` |
| `ValueFlowValidator` | Workflow-scope graph | `$workflow/variables/{Name}` etc. · `ValueFlow/*` (ConcurrentWrite, UnavailableProducer, ScopeBoundary, CyclicBackEdge, UnstableCollectionIdentity) |

Catalog-consulting validators resolve `ActivityVersionId`s through the scoped, memoizing
`CatalogVersionResolver` (Internal), which translates the version store's throwing Get contract
into a nullable result — see the Faulting note in `EXTENSION_POINTS.md` before writing a new
catalog-consulting validator.

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
- **Graph-specific validators** such as orphan checks belong to the activity feature that owns
  graph semantics, such as a future Flowchart module.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md)
for the `IDraftValidator` contributor interface and the `OnDraftValidating` / `OnDraftValidated` event surface (Events section).
