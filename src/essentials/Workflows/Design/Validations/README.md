# `Elsa.Workflows.Design.Validations`

Baseline universal validators for the workflow-design sub-domain. Implements the
`IDraftValidator` contributor interface (from `Elsa.Workflows.Design.Validations.Core`)
and **returns** `ValidationError` entries. The single `ExecuteValidations` handler
(also registered here) aggregates every validator's returned errors onto the
`DraftValidating` event's `Errors` collection; the publishing command reads them back and
surfaces them on `DraftValidated` (create/update) or uses them as the promotion gate (FR-024).
Errors are derived state — recomputed on every mutation and re-derived on demand — and are not
persisted.

Per framework §2.22 — this README documents what the feature registers.

---

## Activation

`WorkflowDesignValidationsFeature : IShellFeature` — named `WorkflowDesignValidations`.

## Settings

`WorkflowDesignValidatorOptions` (bound via the feature's `MaxRecursionDepth` property; default `100`).

- `MaxRecursionDepth` — safety net for the iterative activity-tree walker. Validators that
  recurse through activity-owned structure (required-input, variable-expression
  resolver) stop descending past this depth. Iterative DFS internally, so the .NET call stack
  is never the actual risk — the bound guards against cyclic / malformed Draft data.

## Contributor interfaces registered

All of the following implement `IDraftValidator` and are registered via DI
(`services.AddScoped<IDraftValidator, X>()`). Each **returns** its `ValidationError` set from
`Validate(...)`; the single `ExecuteValidations : IEventHandler<DraftValidating>` handler
(registered here) resolves `IEnumerable<IDraftValidator>`, runs each, and adds every returned
error to `event.Errors`. The publishing command reads `event.Errors` back after dispatch and
surfaces them on `DraftValidated` / uses them as the promotion gate; the errors are not
persisted.

| Validator | Scope | `(Path, Type)` emitted |
|---|---|---|
| `UnknownActivityVersionValidator` | Root + nested (recurses) | `{NodeId}` · `Graph/UnknownActivityVersion` |
| `UnhandledActivityStructureValidator` | Root + nested (recurses) | `{NodeId}` · `Graph/UnhandledActivityStructure` |
| `StartActivityValidator` | Root-level | `$workflow` · `RootActivity/Missing` |
| `VariableUniquenessValidator` | Workflow-scope | `$workflow/variables/{Name}` · `Variables/Uniqueness` |
| `RequiredInputOutputValidator` | Catalog-backed root + nested (recurses) | `{NodeId}/inputs/{ReferenceKey}` · `InputOutput/MissingRequired` |
| `VariableExpressionResolverValidator` | Root + nested (recurses) | `{NodeId}/inputs|outputs/{ReferenceKey}` · `Expressions/UnresolvedVariable` |
| `ValueFlowValidator` | Workflow-scope graph | `$workflow/variables/{Name}` etc. · `ValueFlow/*` (ConcurrentWrite, UnavailableProducer, ScopeBoundary, CyclicBackEdge, UnstableCollectionIdentity) |
| `CredentialLiteralValidator` | Catalog-backed root + nested (recurses) | `{NodeId}/inputs/{ReferenceKey}` · `Inputs/CredentialLiteral` (reporting only here; see below) |

Catalog-consulting validators resolve `ActivityVersionId`s through the scoped, memoizing
`CatalogVersionResolver` (Internal), which translates the version store's throwing Get contract
into a nullable result — see the Faulting note in `EXTENSION_POINTS.md` before writing a new
catalog-consulting validator.

## The credential-literal rule (spec 188, FR-008, FR-009)

An input the activity declares a credential (`[ActivityInput(IsCredential = true)]`, read from the catalog's
`InputDefinition.IsCredential`) accepts only a secret reference or no binding. A literal, an object, a request for
the declared default, a value read, any expression, and a `Secret` binding whose payload is not a well-formed reference
(`SecretReferencePayload`: an object with a non-blank text `name`, optional text `typeName` and `scope`, and no other
member) are refused with the rule id `Inputs/CredentialLiteral`; an empty or null literal leaves the input unbound and
is accepted, and so does a `Secret` binding with no payload (publication refuses that one). The acceptance predicate is
`CredentialInputBinding.IsAccepted` in `Elsa.Workflows.Design.Core`, and each refusal is a
`CredentialLiteralFinding`: path `{NodeId}/inputs/{ReferenceKey}`, a message that starts with the rule id and names
the input and the activity node, and never the bound value.

The rule runs only in the application layer, never in a persistence store, at five integration points that cover
FR-008's seven definition entry points and the Elsa 3 collection import, admitted in slice 6's review:

| Integration point | Entry points | On a refusal |
|---|---|---|
| Design API admission (`Elsa.Workflows.Design.Api`): Definitions/Add, Drafts/Replace, Definitions/Update, Versions/Add, Definitions/Submit, Drafts/Promote | draft save, add-version, submit, promote | `CredentialLiteralRefusedException` before the command runs, so nothing is stored (400, errors keyed by path) |
| `WorkflowsVersionReconciler` (`Elsa.Workflows.Design.Reconciliation`), per item | file-based reconciliation, git import | that item only: nothing is written for it, its claim is dropped, a value-free warning is logged, and the pass goes on |
| `GitWorkflowExporter` (`Elsa.Workflows.Design.Reconciliation.Git`), per version | git export | that version only: no file, commit or tag, a value-free warning, and the pass goes on |
| `RuntimeInputBindingCompiler.CompileAll` (`Elsa.Workflows.Publishing`), per node | publish, publish-on-reconcile, draft test runs | `CredentialLiteralRefusedException`, ahead of `VF-ACT-011`, reported as a compile error (400) |
| `ReusableActivityCollectionImporter` (`Elsa3.Activities.Design.Import`), every node it maps, per apply | Elsa 3 collection import | the whole apply, which is all or nothing: one `CredentialLiteralRefusedException` before the commit, so no workflow or activity is committed (400, the messages in the problem's `detail`); the refusal also deletes the upload the import ledger stored, unless that delete fails (#2357, #2375) |

Every caller of the first three and the last takes the rule's contract, `ICredentialLiteralValidator`
(`Elsa.Workflows.Design.Validations.Core`). A Design API caller admits the incoming or stored state through
`WorkflowStateAdmission.AdmitAsync`, a static helper over the contract (like `DraftValidationGate`) that throws the
refusal and holds no rule of its own. The Elsa 3 importer reads the findings for every node it maps, nested ones
included, and throws one refusal holding all of them; the reconciler and the exporter read the findings and skip the
item.
Publication applies the predicate itself, because it already holds each input's declaration. Draft save blocking on this rule is the one deliberate exception to "draft save records validation
errors without blocking": the exception is the Design API's admission, and `DraftValidating` stays non-blocking for
every validator, this one included.

The validator judges only a node whose activity version the catalog holds, down to `MaxRecursionDepth`, as the other
tree-walking validators do, and reaches only the children a registered structure handler projects. A child under a
structure kind with no registered handler is therefore not judged, and publication refuses it only when the parent's
activity declares that structure kind; otherwise the executable compiler compiles the structure opaque, without compiling or
judging the child (the credential-literal contract's Known gaps). The Elsa 3 collection import enumerates the children
its own mapping nests and judges each. A node of an activity that is not installed cannot be judged until publication, which refuses it because it cannot compile it; until then a literal on
it can be stored and exported (the residual of research R7). `UnknownActivityVersionValidator` reports such a node.
Promote judges the draft it reads and passes that draft's `WorkflowDraftStateHash` to the promotion command, which
refuses with a conflict (409) when the draft changed in between, so it promotes exactly what was admitted.

`WorkflowDesignValidations` registers the validator once, as `ICredentialLiteralValidator` and as an `IDraftValidator`
(so the validation panel reports the same findings; no entry point relies on that registration). `WorkflowsDesignApi`, `JsonWorkflowReconciliation` and `WorkflowsDesignGitReconciliation`
declare `DependsOn` on this feature, and so does `Elsa3ImportJsonActivities`. A host that composes a second
`ICredentialLiteralValidator` does not start: a Prepare-phase shell initializer fails activation with
`MultipleCredentialLiteralValidatorsException`, naming every registration, whatever order they were registered in.

## Tasks registered

None (no startup / recurring / scheduled tasks). The feature registers one shell initializer, the
`ICredentialLiteralValidator` composition check above.

## Notes

- **Variable lookup is by `ReferenceKey`, not `Name`** — the id is stable across renames; the
  name is mutable. The variable-expression validator compares
  `ArgumentValue.Value` (the serialised variable id) against `VariableDefinition.ReferenceKey`.
- **Workflow-level required-input/output check** is deliberately a no-op in Unit C. The design
  surface for `WorkflowDefinitionState.Inputs` / `Outputs` carries the `IsRequired` flag (per
  FR-036) but no default value or internal binding to validate against; the actionable
  workflow-level semantic is downstream (Unit D / E). Activity-level coverage is complete.
- **Activity outputs are optional captures.** `OutputDefinition.IsRequired` describes the result
  contract produced by the activity; it does not require every authored node to bind/capture that
  result. The baseline validator therefore checks required activity inputs only.
- **Engine intrinsics do not resolve through the catalog.** Intrinsic nodes are validated by their
  dedicated validators and compiler and are skipped by catalog-consulting validators.
- **Activity-specific validators** ship in their owning activity feature per FR-034 (e.g.
  `Elsa.Http.Activities.Design` ships HTTP validators), NOT here.
- **Graph-specific validators** such as orphan checks belong to the activity feature that owns
  graph semantics, such as a future Flowchart module.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md)
for the `IDraftValidator` contributor interface and the `DraftValidating` / `DraftValidated` event surface (Events section).
