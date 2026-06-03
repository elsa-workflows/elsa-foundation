# Extension points — `Elsa.Workflows.Design.Validations.Core`

The per-domain catalog (framework §2.22.1) of everything you can implement or override in this domain, plus the events it publishes. Three sections:

- **Overridable contracts** — `.Core` interfaces with a default implementation you can *replace* (`services.Replace(...)` / register-your-own). You bring one implementation and the built-in one steps aside.
- **Implementable contributor interfaces** — *add-don't-replace* seams. You register an additional implementation alongside any others; a single aggregating handler runs them all (framework §2.6.1, §2.24.2).
- **Events** — what this `.Core` publishes (category, semantic, payload, strategy, publication site, expected handlers, ordering). Events are the dispatch mechanism behind the contributor interfaces and the observation surface for subscribers.

This is the repo-wide [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md) index's entry for this domain; the index links here for detail.

---

## Overridable contracts

This `.Core` exposes no swappable default-impl service of its own — the validation *behaviour* is contributed (see below), and the validation *outcome* is persisted by whichever command owns the transition. The validation-result sibling contract `IWorkflowDefinitionDraftValidation` is a read-model abstraction realised by the persistence layer, not a behavioural seam.

---

## Implementable contributor interfaces

### `IDraftValidator`
- **Kind:** Validator (action-named contributor — inspects and **returns** findings). **Lives in:** `Elsa.Workflows.Design.Validations.Core` (`Contracts/`).
- **Signature:** `ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken);`
- **Returns** the validation errors it found (empty when valid); it never mutates the event.
- **Register:** `services.AddScoped<IDraftValidator, MyValidator>()`.
- **Consumed by:** the single `ExecuteValidations : IEventHandler<OnDraftValidating>` (`Elsa.Workflows.Design.Validations`), which injects `IEnumerable<IDraftValidator>` and aggregates every implementation's errors onto the event's `Errors` collection.
- **Adding one does not replace the others:** the 5 baseline validators and every activity-feature-co-located validator all run. This is the *extend* path, not the *override* path.

---

## Events

Both events are `IEvent` (framework §2.6.1); they differ only in **delivery strategy** (§2.6.6). This `.Core` is the canonical mixed-strategy example.

Heading convention per research item R4: `### <EventClassName>`. The catalog-parity test scans every `IEvent` type in this `.Core` assembly and asserts bidirectional alignment with the `### On…` headings in this section.

**Sequential / contribution** (§2.6.6) — `OnDraftValidating`. The gate. Features implement the `IDraftValidator` contributor interface and return their errors; the single `ExecuteValidations` handler aggregates them onto the event's directly-accessible `Errors` collection. The mutation pipeline publishes it Sequential, awaits the dispatch, and reads the collected errors back.

### OnDraftValidating

**Semantic.** Mutation gate. The post-mutation Draft snapshot is presented for validation BEFORE the state is persisted. Validators implement `IDraftValidator` and **return** their errors; the single `ExecuteValidations` handler runs every validator and aggregates the returned errors onto the event's `Errors` collection. The publisher (the mutation pipeline) reads `event.Errors` after dispatch and persists them to the `WorkflowDefinitionDraftValidation` sibling in the same transaction as the state.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft (cross-`.Core` reference to `Elsa.Workflows.Design.Core` per framework §2.1).
- `Errors : ICollection<ValidationError>` — a directly-accessible collection the aggregating handler writes into. Individual validators never touch the event; they return errors via `IDraftValidator.Validate`.

**Contributor interface.** `IDraftValidator` (this `.Core`) — see the Implementable contributor interfaces section above for signature + registration.

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

**Background / notification** (§2.6.6) — `OnDraftValidated`. The outcome notification. Published Background after the validation pass completed and the errors were persisted alongside the state. Audit, UI push, telemetry react.

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

- Granular FR-018 / FR-018a mutation events live in [`Elsa.Workflows.Design.Core/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Core/EXTENSION_POINTS.md).
- The validation pair is published by whichever command owns the transition: `OnDraftValidating` (Sequential gate) then `OnDraftValidated` (Background outcome) fire from `CreateDraftCommand` (origination + clone, by delegation) and `UpdateDraftCommand` (mutation); see each command's doc-header for the in-lock order. There is no shared "pipeline" collaborator — each command owns its shell inline.
- Repo-wide interface index: [`EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 (the single `IEvent` concept + contribution sub-pattern; action-named contributor suffixes) + §2.6.6 (delivery strategies) + §2.22.1 (per-domain extension-points catalog) + §2.24.2 (contributor interface + single aggregating handler).
