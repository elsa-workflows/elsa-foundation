# Extension points — Workflows.Design.Validations domain

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the draft-validation sub-domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Validations` — the composition root where `WorkflowDesignValidationsFeature` wires the aggregating handler `ExecuteValidations` and the four built-in baseline validators. Three sections:

- **Overridable contracts**: `ICredentialLiteralValidator`, the credential-literal rule (spec 188).
- **Implementable contributor interfaces** — the `IDraftValidator` add-don't-replace seam.
- **Events** — the validation gate (`DraftValidating`) and outcome notification (`DraftValidated`).

A closing note describes the credential-literal admission helper and the rule's exceptions, which are not extension points.

---

## Overridable contracts

The draft-validation *behavior* is contributed (see below); the validation *outcome* is derived state, recomputed in-lock on every create/update mutation and re-derived by the promotion gate. It is not persisted, so there is no read-model abstraction or storage seam to override. The one swappable service is the credential-literal rule.

### `ICredentialLiteralValidator` *(Core, `Elsa.Workflows.Design.Validations.Core`)*
- **Kind:** Validator (returns findings), a single-implementation **replacement contract** (framework §2.6.2), declared by `[CredentialLiteralValidatorReplacementContract]`.
- **Signature:** `ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken);`
- **Returns** one `CredentialLiteralFinding` per binding the rule refuses (spec 188, FR-008): a literal, object, default request, value read or expression on an input the activity declares a credential. Path `{NodeId}/inputs/{ReferenceKey}`, type `Inputs/CredentialLiteral`, a message that starts with the rule id and never carries the bound value. A node whose activity version the catalog does not hold returns no finding.
- **Default:** `CredentialLiteralValidator` (this feature), which applies `CredentialInputBinding.IsAccepted` (`Elsa.Workflows.Design.Core`) to every catalog-backed node, root and nested (the children a registered structure handler projects), down to `MaxRecursionDepth`.
- **Register:** `WorkflowDesignValidations` registers the default. A host that composes a second implementation does not start: a Prepare-phase shell initializer fails activation with `MultipleCredentialLiteralValidatorsException`, naming every registration, in either registration order.
- **Consumed by:** the application-layer writers of workflow state the architecture suite's coverage guard knows of, each taking it as a constructor dependency: every constructor outside the design persistence project that takes a design command writing workflow state (the six Design API callers and `WorkflowsVersionReconciler`), every `IGitWorkflowExporter` implementation, and the Elsa 3 collection import's `ReusableActivityCollectionImporter`. The guard (`ArchitectureGuardTests`, `Every_*` tests in `ArchitectureGuardTests.CredentialLiteralAdmission.cs`) fails when a new caller takes such a command or the Elsa 3 import's commit port without it, and when a command contract is left unclassified. A writer that refuses a whole request (a Design API caller, the Elsa 3 import) admits through `WorkflowStateAdmission.AdmitAsync`; the reconciler and the exporter read the findings and skip the item. Publication does not consume it: the compiler applies `CredentialInputBinding.IsAccepted` itself.

---

## Implementable contributor interfaces

### `IDraftValidator` *(Core — `Elsa.Workflows.Design.Validations.Core`)*
- **Kind:** Validator (action-named contributor — inspects and **returns** findings).
- **Signature:** `ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken);`
- **Returns** the validation errors it found (empty when valid); it never mutates the event.
- **Register:** `services.AddScoped<IDraftValidator, MyValidator>()`.
- **Aggregated by:** the single `ExecuteValidations : IEventHandler<DraftValidating>` (this feature), which injects `IEnumerable<IDraftValidator>` and aggregates every implementation's errors onto the event's `Errors` collection.
- **Adding one does not replace the others:** all registered validators run. This is the *extend* path, not the *override* path.
- **Faulting:** an exception escaping `Validate` propagates and faults the whole validation gate (fail-closed). In particular, `IActivityDefinitionLookup.GetVersion` **throws** `EntityNotFoundException` on a missing id — it never returns null. Catalog-consulting validators should resolve versions via `CatalogVersionResolver` (scoped; memoizes per pass and short-circuits blank ids), or call the nullable `IActivityDefinitionLookup.FindVersion` directly (null = absent); either way **skip** unresolvable nodes — the `Graph/UnknownActivityVersion` report is owned by `UnknownActivityVersionValidator`, so skipping neither hides the problem nor double-reports it. Do **not** call `GetVersion` from a validator: a missing id there faults the gate with an opaque, node-anonymous exception.

**Known implementations (shipped):**
- `Elsa.Workflows.Design.Validations` — `UnknownActivityVersionValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `StartActivityValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `VariableUniquenessValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `RequiredInputOutputValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `VariableExpressionResolverValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations`: `CredentialLiteralValidator` *(intra-domain default; reports the credential-literal findings on the validation panel; the rule is enforced through `ICredentialLiteralValidator` by the writers listed under that contract, not by this registration)*
- Activity feature validators *(cross-domain — each activity feature ships its own `IDraftValidator` per FR-034)*
- Graph-specific validators such as orphan checks belong to the activity feature that owns graph semantics, such as a future Flowchart module.

---

## Events

Both events are `IEvent` (framework §2.6.1); they differ only in **delivery strategy** (§2.6.6). This domain is the canonical mixed-strategy example.

`CatalogParityTests` scans every `IEvent` type in `Elsa.Workflows.Design.Validations.Core` and asserts bidirectional alignment with the `### On…` headings in this section.

**Sequential / contribution** (§2.6.6) — `DraftValidating`. The gate. Features implement `IDraftValidator` and return their errors; the single `ExecuteValidations` handler aggregates them onto the event's `Errors` collection. The publishing command publishes it Sequential, awaits dispatch, and reads collected errors back.

### DraftValidating

**Semantic.** Mutation gate. The post-mutation Draft snapshot is presented for validation BEFORE the state is persisted. Validators implement `IDraftValidator` and **return** their errors; `ExecuteValidations` aggregates them onto `event.Errors`. The publishing command reads `event.Errors` back after dispatch and surfaces them on `DraftValidated` (create/update) or uses them as the promotion gate (FR-024). Errors are derived state, not persisted.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft.
- `Errors : ICollection<ValidationError>` — the directly-accessible collection `ExecuteValidations` writes into.

**Contributor interface.** `IDraftValidator` (above) — implement + register to add a validator.

**Delivery strategy.** Sequential — the publisher must read contributions back.

**Publication site.** Every mutation command, synchronously inside the per-Draft lock, after the mutation hook runs and before `SaveChangesAsync`.

**Expected handler.** Exactly one `IEventHandler<DraftValidating>`: `ExecuteValidations` (this feature).

**Ordering guarantees.** Fires AFTER the mutation hook applies its in-memory mutation; BEFORE `SaveChangesAsync`. Validators run in DI-resolution order (no guaranteed inter-validator ordering). A validator that throws fails the publish and the mutation (Sequential ships no exception-shielding per §2.6.6).

**Background / notification** (§2.6.6) — `DraftValidated`. Outcome notification published after the mutation is persisted.

### DraftValidated

**Semantic.** The validation pass completed and carries the derived errors (or empty set). Past-tense counterpart to `DraftValidating`. Audit, UI push (SignalR), telemetry react.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the same post-mutation Draft snapshot validators saw.
- `Errors : IReadOnlyList<ValidationError>` — the derived error set (may be empty).
- `HasErrors : bool` — derived convenience accessor.

**Delivery strategy.** Background — fired after the transition is persisted; subscribers must not break the publisher.

**Publication site.** Every mutation command (and `ICreateDraftCommand`), after `SaveChangesAsync` and after the per-Draft lock has been released.

**Ordering guarantees.** FIFO at enqueue. A subscriber exception is caught + logged; it never breaks the publisher. (Per-diff FR-018 mutation events are not published today — see the cross-reference below — so `DraftValidated` is the only post-mutation event on the create/update path.)

---

## The credential-literal admission helper and exceptions

Not extension points: nothing here can be registered, replaced or contributed.

- `WorkflowStateAdmission` *(Core, `Elsa.Workflows.Design.Validations.Core`)*: a static helper over `ICredentialLiteralValidator`, like `DraftValidationGate`. `Task AdmitAsync(this ICredentialLiteralValidator validator, WorkflowDefinitionState state, CancellationToken cancellationToken)` throws `CredentialLiteralRefusedException` when the validator returns a finding; a caller calls it before its write, so a refused state is not stored. It holds no rule of its own.
- `CredentialLiteralRefusedException` *(Core, `Elsa.Workflows.Design.Validations.Core.Exceptions`)*: an `ArgumentException` carrying `Findings`; its message joins the findings' messages. The Design API maps it to 400 with `errors` keyed by each finding's path; publication reports it as a compile error (400); the Elsa 3 import answers 400 with the messages in `detail`.
- `MultipleCredentialLiteralValidatorsException` *(Core, `Elsa.Workflows.Design.Validations.Core.Exceptions`)*: the startup diagnostic for a host that composes more than one `ICredentialLiteralValidator`. It fails shell activation and names every registration in `Implementations` (implementation type, registered instance's type, or "a factory registration").

---

## Cross-references

- Granular FR-018 mutation events (declared as tested contract; publication currently retired pending an event-sourcing consumer): [`Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Api/EXTENSION_POINTS.md).
- Design persistence provider (EF Core stores + commands): [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1 + §2.24.2.
