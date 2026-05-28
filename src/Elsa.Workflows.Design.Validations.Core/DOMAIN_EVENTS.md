# Domain Events — `Elsa.Workflows.Design.Validations.Core`

Catalog per framework §2.22.1 + Unit C FR-030. Heading convention per research item R4:
`### <EventClassName>`. The catalog-parity test in
`tests/Elsa.Workflows.Design.Tests/Unit/CatalogParityTests.cs` (Unit C FR-031) asserts
bidirectional alignment between this file and the assembly's `IDomainEvent` types.

**Pipeline behaviour** (framework §2.6.1 + Unit C FR-027c):
- Default dispatcher: `Iterator → ExceptionShielding → Invoker`.
- Per-handler exceptions caught + logged + swallowed; dispatch always completes.
- Subscribers MUST NEVER break the publisher.

---

### OnDraftValidating

**Semantic.** Coarse validation-pass event. Fires after every granular FR-018 Draft mutation; validators subscribe to this and contribute errors via `AddValidationError`. Per Unit C FR-025.

**Payload.**
- `Draft : IWorkflowDefinitionDraft` — the post-mutation Draft (cross-`.Core` reference to `Elsa.Workflows.Design.Core` per framework §2.1).
- Private backing: `_errors : List<ValidationError>`.
- **Contribution API:** `void AddValidationError(ValidationError error)`.
- **Read accessor:** `public IReadOnlyList<ValidationError> Errors` — non-mutating by type per framework §2.6.1.

**Publication site.** Every FR-019 mutation command, after the granular FR-018 event for the same mutation.

**Expected handlers.**
- The 5 baseline validators in `Elsa.Workflows.Design.Validations` (per Unit C FR-033): orphan-activity, missing/duplicate-start, variable-uniqueness (case-insensitive), required-input/output, variable-expression-resolver.
- Activity-feature-co-located validators per Unit C FR-034 (each activity feature ships its own `IDomainEventHandler<OnDraftValidating>` that recognises its activity types — e.g. `Elsa.Http` ships HttpEndpoint authorisation-policy lookup, URL validation, etc., inside `Elsa.Http.Activities.Design` per Elsa constitution §E3.10).

**Ordering guarantees.**
- Fires after the granular FR-018 event for the same mutation.
- Validators run in DI-resolution order (no guaranteed inter-validator ordering — independent per framework §2.6.1).
- The publishing command reads `event.Errors` *after* the handler chain completes (guaranteed to complete per FR-027c), then flushes wholesale to `WorkflowDefinitionDraftValidation` per FR-023 delete-and-re-add.

**Cross-references.** Granular mutation events live in [`Elsa.Workflows.Design.Core/DOMAIN_EVENTS.md`](../Elsa.Workflows.Design.Core/DOMAIN_EVENTS.md): each fires before this event for the same mutation.
