# Extension points — Workflows.Design.Validations domain

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the draft-validation sub-domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Validations` — the composition root where `WorkflowDesignValidationsFeature` wires the aggregating handler `ExecuteValidations` and its baseline validators. Three sections:

- **Overridable contracts**: `ICredentialLiteralValidator` and the scoped `IActivityDataSelectionAnalyzer` authority.
- **Implementable contributor interfaces** — draft validators, provider-owned analysis projections and input-source restrictions.
- **Events** — validation, input-constraint collection and outcome notification.

A closing note describes the credential-literal admission helper and the rule's exceptions, which are not extension points.

---

## Overridable contracts

The draft-validation *behavior* is contributed (see below); the validation *outcome* is derived state, recomputed in-lock on every create/update mutation and re-derived by the promotion gate. It is not persisted, so there is no read-model abstraction or storage seam to override.

### `IActivityDataSelectionAnalyzer` *(Core — `Elsa.Workflows.Design.Validations.Core`)*

- **Kind/signature:** one scoped replacement authority; `ValueTask<ActivityDataSelectionAnalysis> AnalyzeAsync(ActivityDataSelectionDocument document, ActivityDataSelectionTarget? target = null, ActivityDataSelectionQuery? query = null, CancellationToken cancellationToken = default)`.
- **Default/registration:** `ActivityDataSelectionAnalyzer`, installed with `TryAddScoped`. A host may replace that one scoped registration. `DataSelectionAnalyzerCompositionCheck` rejects missing, duplicate or non-scoped authority at shell Prepare; copied shells inspect their own final registrations.
- **Consumers:** `ValueFlowValidator`, authorized Workflow/Activity Design metadata coordinators, the existing expression-symbol adapter, and mandatory workflow/template/source-owned publication and test-run admission. Resource owners authorize the actual candidate/catalog before analysis; the analyzer reads no HTTP authority and creates no draft, runtime value or persistence write.
- **Facts:** structure handlers retain control-flow ownership. Serialized shapes come through Activity Design Core's `IActivitySerializedShapeSource`; multiple matching owners fail closed. A null result-source `Binding` with `RuntimeScopeBindingUnavailable` retains metadata without inventing a direct runtime scope. `ActivityResultSource` separately supplies the established portable-expression identity. Member-runtime support remains outside this Foundation slice.
- **Composition:** `WorkflowDesignValidations` depends on `Events`. The required `IInlineEventPublisher` dispatches constraint collection through the canonical Sequential pipeline; missing publisher is a composition failure, not permission to ignore contributors.

### `ICredentialLiteralValidator` *(Core, `Elsa.Workflows.Design.Validations.Core`)*
- **Kind:** Validator (returns findings), a single-implementation **replacement contract** (framework §2.6.2), declared by `[CredentialLiteralValidatorReplacementContract]`.
- **Signature:** `ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken);`
- **Returns** one `CredentialLiteralFinding` per binding the rule refuses (spec 188, FR-008): a literal, object, default request, value read, expression, or `Secret` binding whose bound payload is not a well-formed `SecretReferencePayload`, on an input the activity declares a credential. Path `{NodeId}/inputs/{ReferenceKey}`, type `Inputs/CredentialLiteral`, a message that starts with the rule id and never carries the bound value. A node whose activity version the catalog does not hold returns no finding.
- **Default:** `CredentialLiteralValidator` (this feature), which applies `CredentialInputBinding.IsAccepted` (`Elsa.Workflows.Design.Core`) to every catalog-backed node, root and nested (the children a registered structure handler projects), down to `MaxRecursionDepth`.
- **Register:** `WorkflowDesignValidations` registers the default. To replace it, swap the one registration after the feature has registered: `services.Replace(ServiceDescriptor.Scoped<ICredentialLiteralValidator, MyValidator>())`. The default's separate `IDraftValidator` registration stays, so the validation panel still reports the default's findings. A plain additional registration (`services.AddScoped<ICredentialLiteralValidator, MyValidator>()`, before or after the feature) leaves two, and the host does not start: a Prepare-phase shell initializer fails activation with `MultipleCredentialLiteralValidatorsException`, naming every registration, in either registration order (`ValidationsFeatureRegistrationTests` pins both).
- **Consumed by:** the application-layer writers of workflow state the architecture suite's coverage guard knows of, each taking it as a constructor dependency: every constructor outside the design persistence project that takes a design command writing workflow state (the six Design API callers and `WorkflowsVersionReconciler`), every `IGitWorkflowExporter` implementation, and the Elsa 3 collection import's `ReusableActivityCollectionImporter`. The guard (`ArchitectureGuardTests`, `Every_*` tests in `ArchitectureGuardTests.CredentialLiteralAdmission.cs`) fails when a new caller takes such a command or the Elsa 3 import's commit port without it, and when a command contract is left unclassified. A Design API caller refuses its whole request through `WorkflowStateAdmission.AdmitAsync`; the Elsa 3 import reads the findings for every node it maps, nested ones included, and refuses the whole apply with one `CredentialLiteralRefusedException`; the reconciler and the exporter read the findings and skip the item. Publication does not consume it: the compiler applies `CredentialInputBinding.IsAccepted` itself.

---

## Implementable contributor interfaces

### `IActivityDataSelectionDocumentProjector` *(Core — `Elsa.Workflows.Design.Validations.Core`)*

- **Signature:** `ValueTask<ActivityDataSelectionDocument?> TryProjectAsync(ActivityProviderManifest manifest, ActivityContract contract, string resourceId, string contractRevision, CancellationToken cancellationToken = default)`; the contract argument is the Activities Design Core public contract.
- **Registration/ownership:** provider features contribute implementations through DI. Null means another provider/schema owns the manifest; exactly one matching projection is permitted. Malformed owned content fails instead of disappearing as unsupported. The coordinator passes the actual resource/revision after authorization; projection performs no store reads or synthetic workflow-draft creation.
- **Known implementation:** `GraphActivityProvider`, singleton contribution from `Elsa.Activities.Graph.Design` *(cross-domain)*. It projects its closed graph body/public contract/variables and does not capture the scoped analyzer. Activity Design API and `ActivityTemplateCompiler` consume the contribution from their own scope.

### `IActivityInputSourceConstraintProvider` *(Core — `Elsa.Workflows.Design.Validations.Core`)*

- **Signature:** `ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(IActivityDefinitionVersion activityVersion, string inputKey, CancellationToken cancellationToken = default)`.
- **Registration:** additive scoped contributors, normally `services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityInputSourceConstraintProvider, MyProvider>())`; null adds no restriction. These are metadata contributions for an owner-resolved version/input, not a UI hint, grant or executable binding.
- **Aggregation:** the single `CollectInputConstraints : IEventHandler<InputConstraintsCollecting>` awaits each registered provider and fills the event's get-only collection. The analyzer publishes inline, awaits contributions, and intersects them with existing credential/connection restrictions; no contribution can weaken another. Exceptions propagate and prevent a successful analysis/admission.
- **Known implementation:** `DecisionInputSourceConstraintProvider`, from `Elsa.Decisions.Workflows.Design` *(cross-domain)*. It exposes the literal-at-publication Decision selector restriction; `DecisionPinSource` remains the final publication authority. Ordinary decision inputs are unrestricted by that contribution.

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

All events are `IEvent` (framework §2.6.1); their **delivery strategy** (§2.6.6) determines whether the publisher reads contributions back.

Every `IEvent` type in `Elsa.Workflows.Design.Validations.Core` has a `### <EventClassName>` heading in this section, and every such heading names one; review keeps the two in step.

### InputConstraintsCollecting

**Semantic/payload.** Metadata contribution phase for one actual catalog-backed target: `ActivityVersion : IActivityDefinitionVersion`, `InputKey : string`, and get-only `Constraints : ICollection<ActivityDataInputConstraint>`. It contains neither bound business values nor authorization claims.

**Publication/delivery.** `ActivityDataSelectionAnalyzer.Target` publishes through required `IInlineEventPublisher` (canonical Sequential) before reading/intersecting constraints. Target facts are cached within one analysis, so a target collects once per pass; intrinsics without a catalog version do not publish this event.

**Expected handler/contributors.** Exactly one aggregating handler, internal `CollectInputConstraints`, registered idempotently by `WorkflowDesignValidationsFeature`. Modules implement `IActivityInputSourceConstraintProvider`, not separate collecting-event handlers. Provider calls are awaited in DI order; intersection grants no priority, and a failure propagates without shielding. This phase performs no save or publication write.

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

- Granular FR-018 mutation events (declared as tested contract; publication currently retired pending an event-sourcing consumer): [`Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`](../../Api/EXTENSION_POINTS.md).
- Design persistence provider (EF Core stores + commands): [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1 + §2.24.2.
