# Extension points — Workflows.Design.Validations domain

The per-domain catalog (framework §2.22.1) of everything you can implement or override in the draft-validation sub-domain, plus the events it publishes. Anchored at `Elsa.Workflows.Design.Validations` — the composition root where `WorkflowDesignValidationsFeature` wires the aggregating handler `ExecuteValidations` and the four built-in baseline validators. Three sections:

- **Overridable contracts** — none in this domain.
- **Implementable contributor interfaces** — the `IDraftValidator` add-don't-replace seam.
- **Events** — the validation gate (`OnDraftValidating`) and outcome notification (`OnDraftValidated`).

---

## Overridable contracts

This domain exposes no swappable default-impl service. The validation *behaviour* is contributed (see below); the validation *outcome* is derived state — recomputed in-lock on every create/update mutation and re-derived by the promotion gate. It is not persisted, so there is no read-model abstraction or storage seam to override.

---

## Implementable contributor interfaces

### `IDraftValidator` *(Core — `Elsa.Workflows.Design.Validations.Core`)*
- **Kind:** Validator (action-named contributor — inspects and **returns** findings).
- **Signature:** `ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken);`
- **Returns** the validation errors it found (empty when valid); it never mutates the event.
- **Register:** `services.AddScoped<IDraftValidator, MyValidator>()`.
- **Aggregated by:** the single `ExecuteValidations : IEventHandler<OnDraftValidating>` (this feature), which injects `IEnumerable<IDraftValidator>` and aggregates every implementation's errors onto the event's `Errors` collection.
- **Adding one does not replace the others:** all registered validators run. This is the *extend* path, not the *override* path.

**Known implementations (shipped):**
- `Elsa.Workflows.Design.Validations` — `StartActivityValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `VariableUniquenessValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `RequiredInputOutputValidator` *(intra-domain — default)*
- `Elsa.Workflows.Design.Validations` — `VariableExpressionResolverValidator` *(intra-domain — default)*
- Activity feature validators *(cross-domain — each activity feature ships its own `IDraftValidator` per FR-034)*
- Graph-specific validators such as orphan checks belong to the activity feature that owns graph semantics, such as a future Flowchart module.

---

## Events

Both events are `IEvent` (framework §2.6.1); they differ only in **delivery strategy** (§2.6.6). This domain is the canonical mixed-strategy example.

`CatalogParityTests` scans every `IEvent` type in `Elsa.Workflows.Design.Validations.Core` and asserts bidirectional alignment with the `### On…` headings in this section.

**Sequential / contribution** (§2.6.6) — `OnDraftValidating`. The gate. Features implement `IDraftValidator` and return their errors; the single `ExecuteValidations` handler aggregates them onto the event's `Errors` collection. The publishing command publishes it Sequential, awaits dispatch, and reads collected errors back.

### OnDraftValidating

**Semantic.** Mutation gate. The post-mutation Draft snapshot is presented for validation BEFORE the state is persisted. Validators implement `IDraftValidator` and **return** their errors; `ExecuteValidations` aggregates them onto `event.Errors`. The publishing command reads `event.Errors` back after dispatch and surfaces them on `OnDraftValidated` (create/update) or uses them as the promotion gate (FR-024). Errors are derived state, not persisted.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft.
- `Errors : ICollection<ValidationError>` — the directly-accessible collection `ExecuteValidations` writes into.

**Contributor interface.** `IDraftValidator` (above) — implement + register to add a validator.

**Delivery strategy.** Sequential — the publisher must read contributions back.

**Publication site.** Every mutation command, synchronously inside the per-Draft lock, after the mutation hook runs and before `SaveChangesAsync`.

**Expected handler.** Exactly one `IEventHandler<OnDraftValidating>`: `ExecuteValidations` (this feature).

**Ordering guarantees.** Fires AFTER the mutation hook applies its in-memory mutation; BEFORE `SaveChangesAsync`. Validators run in DI-resolution order (no guaranteed inter-validator ordering). A validator that throws fails the publish and the mutation (Sequential ships no exception-shielding per §2.6.6).

**Background / notification** (§2.6.6) — `OnDraftValidated`. Outcome notification published after the mutation is persisted.

### OnDraftValidated

**Semantic.** The validation pass completed and carries the derived errors (or empty set). Past-tense counterpart to `OnDraftValidating`. Audit, UI push (SignalR), telemetry react.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the same post-mutation Draft snapshot validators saw.
- `Errors : IReadOnlyList<ValidationError>` — the derived error set (may be empty).
- `HasErrors : bool` — derived convenience accessor.

**Delivery strategy.** Background — fired after the transition is persisted; subscribers must not break the publisher.

**Publication site.** Every mutation command (and `ICreateDraftCommand`), after `SaveChangesAsync` and after the per-Draft lock has been released.

**Ordering guarantees.** FIFO at enqueue. A subscriber exception is caught + logged; it never breaks the publisher. (Per-diff FR-018 mutation events are not published today — see the cross-reference below — so `OnDraftValidated` is the only post-mutation event on the create/update path.)

---

## Cross-references

- Granular FR-018 mutation events (declared as tested contract; publication currently retired pending an event-sourcing consumer): [`Elsa.Workflows.Design.Api/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Api/EXTENSION_POINTS.md).
- Persistence-lifecycle seams: [`Elsa.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Persistence.EFCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1 + §2.24.2.
