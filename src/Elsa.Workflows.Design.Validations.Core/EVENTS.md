# Events — `Elsa.Workflows.Design.Validations.Core`

Catalog per framework §2.22.1. Both events are `IEvent` (framework §2.6.1); they differ only in **delivery strategy** (§2.6.6). This `.Core` is the canonical mixed-strategy example.

**Sequential / contribution** (§2.6.6) — `OnDraftValidating`. The gate. Features implement the `IDraftValidator` contributor interface and return their errors; the single `ExecuteValidations` handler aggregates them onto the event's directly-accessible `Errors` collection. The mutation pipeline publishes it Sequential, awaits the dispatch, and reads the collected errors back.

**Background / notification** (§2.6.6) — `OnDraftValidated`. The outcome notification. Published Background after the validation pass completed and the errors were persisted alongside the state. Audit, UI push, telemetry react.

Heading convention per research item R4: `### <EventClassName>`. The catalog-parity test scans every `IEvent` type in this `.Core` assembly and asserts bidirectional alignment with the headings here.

---

## Sequential / contribution events

### OnDraftValidating

**Semantic.** Mutation gate. The post-mutation Draft snapshot is presented for validation BEFORE the state is persisted. Validators implement `IDraftValidator` and **return** their errors; the single `ExecuteValidations` handler runs every validator and aggregates the returned errors onto the event's `Errors` collection. The publisher (the mutation pipeline) reads `event.Errors` after dispatch and persists them to the `WorkflowDefinitionDraftValidation` sibling in the same transaction as the state.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft (cross-`.Core` reference to `Elsa.Workflows.Design.Core` per framework §2.1).
- `Errors : ICollection<ValidationError>` — a directly-accessible collection the aggregating handler writes into. Individual validators never touch the event; they return errors via `IDraftValidator.Validate`.

**Contributor interface.** `IDraftValidator` (this `.Core`):
- `ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)`.
- Return-style contributor (it **returns** its errors; it does not mutate the event). Implement it in any feature and register it via DI (`services.AddScoped<IDraftValidator, MyValidator>()`); the `ExecuteValidations` handler resolves `IEnumerable<IDraftValidator>` and aggregates. Per framework §2.24.2 (contributor interface + single aggregating handler).

**Delivery strategy.** Sequential (the default) — the publisher must read contributions back.

**Publication site.** Every FR-019 mutation command, **synchronously inside the per-Draft lock**, after the mutation hook runs and before `SaveChangesAsync`. The pipeline awaits `IEventPublisher.Publish(..., EventPublishingStrategy.Sequential, ...)` end-to-end.

**Expected handler.**
- Exactly one `IEventHandler<OnDraftValidating>`: `ExecuteValidations` in `Elsa.Workflows.Design.Validations`. It injects `IEnumerable<IDraftValidator>` and aggregates.

**Contributing validators (`IDraftValidator` impls).**
- The 5 baseline validators in `Elsa.Workflows.Design.Validations` (per Unit C FR-033): orphan-activity, missing/duplicate-start, variable-uniqueness (case-insensitive), required-input/output, variable-expression-resolver.
- Activity-feature-co-located validators per Unit C FR-034 (each activity feature ships its own `IDraftValidator` that recognises its activity types and registers it via DI).

**Ordering guarantees.**
- Fires AFTER the mutation hook applies its in-memory mutation.
- Fires BEFORE `SaveChangesAsync` — so validators see post-mutation state, and the publisher can flush the collected errors in the same DB transaction as the state.
- Validators run in DI-resolution order (no guaranteed inter-validator ordering — independent per framework §2.6.1).
- The Sequential path ships **no exception-shielding** (framework §2.6.6, Unit 1): a validator that throws fails the publish and the mutation. Validators are expected to return errors, not throw.

---

## Background / notification events

### OnDraftValidated

**Semantic.** The validation pass completed and the errors (or empty set) are persisted. Past-tense counterpart to `OnDraftValidating` — that one is the gate; this one is the outcome notification. Audit, UI push (signalR), telemetry react to "validation just landed".

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the same post-mutation Draft snapshot validators saw.
- `Errors : IReadOnlyList<ValidationError>` — the persisted error set (may be empty).
- `HasErrors : bool` — derived convenience accessor.

**Delivery strategy.** Background — fired after the transition is persisted; subscribers must not break the publisher.

**Publication site.** Every FR-019 mutation command (and `ICreateDraftCommand`), **after `SaveChangesAsync` and after the per-Draft lock has been released**. Published via `IEventPublisher.Publish(..., EventPublishingStrategy.Background, ...)` — the call returns almost immediately; subscribers run later on the background worker (`BackgroundEventPublisher`).

**Expected handlers.**
- UI push subscribers (signalR / WebSocket) that broadcast the validation outcome so the client renders the error squiggles without polling.
- Audit subscribers that record validation history.
- Telemetry subscribers that track error-rate over time.

**Ordering guarantees.**
- Fires AFTER the corresponding granular FR-018 mutation event for the same mutation (the mutation outcome precedes the validation outcome on the same Draft).
- FIFO at enqueue, preserved at dispatch.
- Cross-Draft ordering is not guaranteed — different Drafts may interleave on the background worker.
- A subscriber exception is caught + logged by the Background strategy + worker; it never breaks the publisher.

---

## Cross-references

- Granular FR-018 / FR-018a mutation events live in [`Elsa.Workflows.Design.Core/EVENTS.md`](../Elsa.Workflows.Design.Core/EVENTS.md).
- The mutation pipeline that publishes both events is `Elsa.Workflows.Design.Persistence.EFCore.Services.DraftMutationPipeline`; see its doc-header for the pipeline order.
- Constitutional basis: §2.6.1 (the single `IEvent` concept + contribution sub-pattern) + §2.6.6 (delivery strategies) + §2.22.1 (events catalog).
