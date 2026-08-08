# Quickstart — Unit C Workflow Design Substrate

> **Supersession note (2026-07-05):** recipes below that rebuild `WorkflowDefinitionDraftValidation.Errors` are superseded — the entity is deleted; validation errors are derived state recomputed in-lock on each mutation (and re-derived by the promotion gate), never persisted (spec.md FR-021/FR-023). Per-diff mutation-event publication is likewise retired (declarations stand). The validators-as-`DraftValidating`-subscribers and wholesale-rebuild *semantics* survive in-memory. Reinstatable when a consumer exists.

Developer onboarding for the Unit C deliverables. Five recipes covering the most common consumer scenarios.

---

## 1. "I want to add a custom validator for my activity"

**Audience:** an activity-feature author (e.g. `Elsa.Http`, `Elsa.Email`, …) who ships an activity that needs validation beyond the baseline (FR-033) checks.

**Recipe.**

1. Reference `Elsa.Workflows.Design.Validations.Core` from your activity's design-time module. Per Elsa §E3.10, that module is `Elsa.<Model>.Activities.Design` (e.g. `Elsa.Http.Activities.Design`).

2. Implement `IDomainEventHandler<DraftValidating>` (`DraftValidating` is in `Elsa.Workflows.Design.Validations.Core`):

   ```csharp
   public sealed class HttpEndpointAuthPolicyValidator : IDomainEventHandler<DraftValidating>
   {
       public async ValueTask Handle(DraftValidating domainEvent, CancellationToken ct)
       {
           foreach (var node in domainEvent.Draft.State.Activities)
           {
               if (node.ActivityVersionId is not "Elsa.Http.HttpEndpoint:1") continue;
               // ... read auth-policy property from node ...
               if (authPolicyIsUnknown)
               {
                   domainEvent.AddValidationError(new ValidationError(
                       Path: $"{node.NodeId}/inputs/AuthPolicy",
                       Type: "Http/AuthPolicyUnknown",
                       Message: $"Authorization policy '{policy}' is not registered. The endpoint will return 403 at runtime."));
               }
           }
       }
   }
   ```

3. Register the handler in your feature's DI registration:

   ```csharp
   services.AddScoped<IDomainEventHandler<DraftValidating>, HttpEndpointAuthPolicyValidator>();
   ```

4. The dispatcher's exception-shielding middleware (framework §2.6.1 default) catches any exception your validator throws — so a bug in your validator can never break the publisher's mutation pipeline. If your validator depends on infrastructure that may fail (file system, network), wrap-and-rethrow per framework §2.23.5 and emit a `ValidationError` for the failure case rather than throwing.

5. Document the handler in your feature's README per framework §2.22 (which events it handles, what errors it can emit).

---

## 2. "I want to add a new mutation command on the Draft"

**Audience:** someone extending the Draft authoring API with a new mutation operation (rare — the FR-019 command set is the canonical surface).

**Recipe.**

1. Define the command contract in `Elsa.Workflows.Design.Persistence.Core/Contracts/`:

   ```csharp
   public interface IMyNewMutationCommand
   {
       Task Execute(MyNewMutationArgs args, CancellationToken ct);
   }
   ```

2. Define a corresponding domain event in `Elsa.Workflows.Design.Core/Events/`:

   ```csharp
   public sealed class OnMyNewMutationInDraft(...) : IDomainEvent { ... }
   ```

3. Implement the command in `Elsa.Workflows.Design.Persistence.EFCore/Commands/`. The implementation MUST:
   - Acquire the per-Draft lock via `IDistributedLockProvider` (key: `workflow-draft:{DraftId}`).
   - Load the Draft + apply the mutation in memory.
   - Publish the granular event.
   - Publish `DraftValidating`.
   - Rebuild `WorkflowDefinitionDraftValidation.Errors` from `event.Errors`.
   - Transactional flush.
   - Release lock.

4. Add the new event to `DOMAIN_EVENTS.md` in `Elsa.Workflows.Design.Core/`. Use heading format `### OnMyNewMutationInDraft` per R4.

5. Add branch-covered tests per framework §2.23.2 in `tests/Elsa.Workflows.Design.Tests/Unit/DraftMutationCommandTests/`.

6. The catalog parity test (FR-031) will fail if you forget step 4. The exception-shielding dispatcher will isolate any handler exceptions per FR-027c.

---

## 3. "How does the validation lifecycle work?"

**Audience:** anyone reading code that mutates the Draft and being puzzled about why errors come and go.

**Concept.** Validators are subscribers to `DraftValidating`, which fires after every Draft mutation. Each validator walks the post-mutation Draft and contributes `ValidationError` entries. The set of errors on the validation sibling (`WorkflowDefinitionDraftValidation.Errors`) is **rebuilt wholesale** after every mutation — there is no "this error was solved" tracking. If the underlying condition is still failing, the next pass re-emits the error; if it stopped failing, the error disappears.

```
prior mutation:  clear the root activity
                 → validators run
                 → Errors: [ ValidationError("$workflow", "RootActivity/Missing", "Workflow has no root activity.") ]

next mutation:   set activity X as the root
                 → validators run
                 → Errors: [ ]   (the root-activity condition is satisfied)

next mutation:   add activity Y whose ActivityVersionId is not in the catalog
                 → validators run
                 → Errors: [ ValidationError($"{Y.NodeId}", "Graph/UnknownActivityVersion", "…does not exist in the activity catalog.") ]
```

**Grouping key for the UI:** `(Path, Type)`. Multiple errors with the same key are grouped under one UI item.

**Promotion gate (FR-024):** `IPromoteDraftToVersionCommand` throws `DraftHasValidationErrorsException` if `Errors.Count > 0`. Successful promotion ⇒ the validation sibling was empty at promote time.

---

## 4. "Where do activity-specific validators live?"

**Short answer:** inside the design-time module of the activity's own domain — per Elsa constitution §E3.10, that's `Elsa.<Model>.Activities.Design` (e.g. `Elsa.Http.Activities.Design`).

**Why not in a separate `Elsa.Workflows.Design.Validations.Http`?** Joey settled this in clarify session 2 Q3: activity-specific validators read activity-specific property shapes, so they share intimate knowledge of the activity definition. Co-locating them with the activity-feature module keeps that knowledge in one place. If a single activity's validator surface grows substantially (10+ validators, complex shared state), refactor-cost test (§2.16) supports extracting a sub-module then.

**The split is:**
- `Elsa.Workflows.Design.Validations` — baseline UNIVERSAL validators (5 of them per FR-033 as amended 2026-07-05) — applicable regardless of activity type. Missing root activity, variable uniqueness, required input/output, variable-expression resolver, unknown activity version.
- `Elsa.<Model>.Activities.Design` — activity-specific validators co-located with the activity definitions (e.g. HttpEndpoint auth-policy lookup, URL string-input validation).

Both subscribe to `DraftValidating` from `Elsa.Workflows.Design.Validations.Core`.

---

## 5. "How does the DOMAIN_EVENTS.md catalog work?"

**Concept.** Every domain whose `.Core` declares contribution or lifecycle events ships a `DOMAIN_EVENTS.md` at the `.Core` project root (framework §2.22.1). It's the discoverable index for "what events does this domain publish?" — humans read it first; AI sessions cite it.

**Format per entry:**

```markdown
### ActivityAddedToDraft

Published when an activity is placed on a Draft's canvas.

**Payload signature:** `DraftId : string`, `NodeId : string`, `ActivityVersionId : string`, `Activity : IActivityNodeView`.

**Published by:** `IAddActivityToDraftCommand`.

**Expected handler audiences:**
- Event-sourcing subscriber (if enabled).
- Activity-feature-co-located validators per FR-034 (handlers that recognise the new ActivityVersionId).

**Ordering:** fires after the snapshot is updated, before `DraftValidating`.

**Cross-references:** `DraftValidating` (Elsa.Workflows.Design.Validations.Core).
```

**Parity test (FR-031):** the test reflection-scans the assembly for all `IDomainEvent` types and parses the `### <EventClassName>` headings from the markdown. Any mismatch fails the build with a precise diagnostic ("event X has no catalog heading" or "catalog heading X has no corresponding event").

**Adding a new event:** add the type + the catalog entry in one commit; the parity test passes.

**Deleting an event:** delete both; the parity test passes.

**Renaming an event:** rename in both; the parity test passes (it just sees a different name on both sides).

---

## 6. Common pitfalls

| Pitfall | Cause | Fix |
|---|---|---|
| Validator handler throws and the Draft mutation silently appears to "succeed" but my error never lands. | Framework §2.6.1 default — subscribers can't break the publisher. Your validator throw was caught, logged, swallowed. | Check the operational logs. Wrap-and-rethrow at your validator's infrastructure boundary per framework §2.23.5; emit a `ValidationError` for the failure condition. |
| Promoted a Draft and discovered the new Version's State doesn't include my latest edits. | Mutations arriving after a promotion's lock acquisition are on the Draft only — they need a new promotion to land on a new Version. | Either: (a) make the next promotion include those edits, or (b) document this for your operators as the expected per-Draft lock semantics. |
| `IsRequired = true` on a workflow-level input but the validator never fires. | The validator walks `WorkflowDefinitionState.Inputs` AND every activity's input declarations. Check that your input is in `State.Inputs`, not nested in an activity. | If the input is workflow-level (workflow-as-activity composition), it's on State. If per-activity, it's under `Activity.Inputs`. |
| Catalog parity test fails after I added a new event. | Step 4 of recipe 2 missed. | Add a `### YourEventName` heading to `DOMAIN_EVENTS.md` with the standard content. |
| New `Elsa.Http.Activities.Design` activity validator fires even when the Draft contains no HTTP activities. | Validator runs on every `DraftValidating` for every Draft (your handler is registered globally). | Inside `Handle`, filter early: `foreach (var node in event.Draft.State.Activities) if (!IsHttp(node)) continue;`. The dispatcher invokes your handler for every event — you decide when to contribute. |

---

## Cross-references

- Spec: [spec.md](./spec.md) — authoritative FR/SC source.
- Plan: [plan.md](./plan.md) — implementation plan with Constitution Check.
- Research: [research.md](./research.md) — plan-stage decisions (R1..R10).
- Data model: [data-model.md](./data-model.md) — entity inventory + lifecycle diagrams.
- Contracts: [commands.md](./contracts/commands.md), [events.md](./contracts/events.md), [read-surfaces.md](./contracts/read-surfaces.md).
- Elsa constitution §E3.10: three-segment secondary-domain naming pattern.
- Framework constitution §2.6.1: domain-events contribution mechanism + subscriber-MUST-NEVER-break-publisher rule.
- Framework constitution §2.22.1: domain-events catalog rule.
