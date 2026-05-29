# Events — `Elsa.Workflows.Design.Validations.Core`

Catalog per framework §2.22.1. This `.Core` ships **one event per category** — it is the canonical mixed-category example for the §2.22.1 split.

**Domain event** (§2.6.1) — `OnDraftValidating`. The gate. Validators handle it and contribute errors via `AddValidationError(...)`; the mutation pipeline awaits the dispatch and reads the collected errors back.

**Lifecycle event** (§2.6.6) — `OnDraftValidated`. The outcome notification. Fires after the validation pass completed and the errors were persisted alongside the state. Audit, UI push, telemetry react.

Heading convention per research item R4: `### <EventClassName>`. The catalog-parity test scans both `IDomainEvent` and `ILifecycleEvent` types in this `.Core` assembly and asserts bidirectional alignment with the headings here.

---

## Domain events (`IDomainEvent`)

### OnDraftValidating

**Semantic.** Mutation gate. The post-mutation Draft snapshot is presented for validation BEFORE the state is persisted. Validators MUST contribute errors via `AddValidationError(ValidationError)`; the publisher (the mutation pipeline) reads `event.Errors` after dispatch and persists them to the `WorkflowDefinitionDraftValidation` sibling in the same transaction as the state.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft (cross-`.Core` reference to `Elsa.Workflows.Design.Core` per framework §2.1).
- Private backing: `_errors : List<ValidationError>`.
- Contribution API: `void AddValidationError(ValidationError error)`.
- Read accessor: `public IReadOnlyList<ValidationError> Errors` — non-mutating by type per framework §2.6.1's intent-revealing-methods sub-rule.

**Publication site.** Every FR-019 mutation command, **synchronously inside the per-Draft lock**, after the mutation hook runs and before `SaveChangesAsync`. The pipeline awaits `IDomainEventSender.Send(...)` end-to-end.

**Expected handlers.**
- The 5 baseline validators in `Elsa.Workflows.Design.Validations` (per Unit C FR-033): orphan-activity, missing/duplicate-start, variable-uniqueness (case-insensitive), required-input/output, variable-expression-resolver.
- Activity-feature-co-located validators per Unit C FR-034 (each activity feature ships its own `IDomainEventHandler<OnDraftValidating>` that recognises its activity types).

**Ordering guarantees.**
- Fires AFTER the mutation hook applies its in-memory mutation.
- Fires BEFORE `SaveChangesAsync` — so validators see post-mutation state, and the publisher can flush the collected errors in the same DB transaction as the state.
- Validators run in DI-resolution order (no guaranteed inter-validator ordering — independent per framework §2.6.1).
- Subscriber exceptions are caught + logged + swallowed by the §2.6.1 default exception-shielding middleware; the publisher always reaches the flush step (Unit C FR-027c).

---

## Lifecycle events (`ILifecycleEvent`)

### OnDraftValidated

**Semantic.** The validation pass completed and the errors (or empty set) are persisted. Past-tense counterpart to `OnDraftValidating` — that one is the gate; this one is the outcome notification. Audit, UI push (signalR), telemetry react to "validation just landed".

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the same post-mutation Draft snapshot validators saw.
- `Errors : IReadOnlyList<ValidationError>` — the persisted error set (may be empty).
- `HasErrors : bool` — derived convenience accessor.

**Publication site.** Every FR-019 mutation command (and `ICreateDraftCommand`), **after `SaveChangesAsync` and after the per-Draft lock has been released**. Dispatched via `ILifecycleEventSender.SendAsync(...)` using the default Background strategy — the call returns almost immediately; subscribers run later on the background worker.

**Expected handlers.**
- UI push subscribers (signalR / WebSocket) that broadcast the validation outcome so the client renders the error squiggles without polling.
- Audit subscribers that record validation history.
- Telemetry subscribers that track error-rate over time.

**Ordering guarantees.**
- Fires AFTER the corresponding granular FR-018 lifecycle event for the same mutation (the mutation outcome precedes the validation outcome on the same Draft).
- FIFO at enqueue, preserved at dispatch.
- Cross-Draft ordering is not guaranteed — different Drafts may interleave on the background worker.

---

## Cross-references

- Granular FR-018 / FR-018a lifecycle events live in [`Elsa.Workflows.Design.Core/EVENTS.md`](../Elsa.Workflows.Design.Core/EVENTS.md).
- The mutation pipeline that publishes both events is `Elsa.Workflows.Design.Persistence.EFCore.Services.DraftMutationPipeline`; see its doc-header for the pipeline order.
- Constitutional split: §2.6.1 (domain events) + §2.6.6 (notifications + lifecycle events) + §2.22.1 (events catalog).
