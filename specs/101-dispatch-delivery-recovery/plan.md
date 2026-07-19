# Implementation Plan: Recover Failed Dispatch Delivery

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #681 and the approved specification in this work unit.

## Summary

Give child-start post-commit work a finite host-configured retry policy and safe delivery-failure classification. Extend the existing fenced final-claim boundary so exhaustion atomically finalizes the same outbox item as the dead letter, projects the dispatch to `DispatchFailed` with deterministic incident/dead-letter evidence, and—for wait mode only—creates the existing deterministic parent-resume responsibility. Add an explicit provider-atomic redrive capability that can reopen only an eligible fire-and-forget failure while preserving the original dispatch, child, intent, and idempotency identities. Extend authenticated dispatch inspection and add a separately managed redrive endpoint without exposing raw failure or payload data.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Workflows Runtime/Core/API/Resumption, DispatchWorkflow Runtime, Persistence Groundwork, Microsoft.Extensions.DependencyInjection, FastEndpoints, Mediator

**Storage**: Existing runtime post-commit outbox, dispatch lifecycle record, checkpoint transaction, and Groundwork document/transaction substrate

**Testing**: xUnit runtime/activity/API tests, Groundwork provider transaction/restart fixtures, race/duplicate tests, safety corpora, and architecture guards

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project runtime library and authenticated operational API

**Performance Goals**: One fenced mutation per attempt; one cross-unit transaction at exhaustion/redrive; bounded failed-dispatch queries; no unbounded scans or workflow-mailbox polling

**Constraints**: Host policy rather than activity inputs; deterministic identities; exactly-once logical wait completion; terminal wait abandonment; safe allowlisted evidence only; separate read/manage authorization; no broker, Studio, #682, #683, or WorkflowDefinitionActivity expansion

**Scale/Scope**: One dead-lettered child-start item per failed dispatch generation; bounded operational listing; at least 100 concurrent duplicate redrive races in verification

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Runtime/Design boundary**: PASS — recovery uses the retained runtime artifact and existing dispatch record; no Design dependency is introduced.
- **Artifact-only execution**: PASS — redrive reuses the committed retained pin and never accepts an authored or API-supplied executable.
- **Checkpoint/post-commit rule**: PASS — exhaustion, dead-letter evidence, lifecycle projection, and optional wait resume commit before acknowledgement.
- **Single-writer/actor boundary**: PASS — redrive only requeues the original start responsibility; workflow state still changes through the existing start/actor path.
- **Contribution semantics**: PASS — the existing child-start contribution remains the single intent-kind/retry-policy source of truth.
- **Provider neutrality**: PASS — retry/finalization/redrive contracts live in Runtime Core; in-memory and Groundwork implement equivalent transitions.
- **Safety**: PASS — durable/API/telemetry evidence is allowlisted; raw exception, provider reason, payload, authority, tenant, and values never cross the boundary.
- **Replay/idempotency**: PASS — the failed outbox item is the dead letter; deterministic incident and generation identities plus fencing reject stale attempts.
- **Authorization**: PASS — inspection retains runtime-read permission while redrive uses the distinct runtime-manage permission and provider tenant scope.
- **Compatibility**: PASS — public activity inputs/outcomes and base store contracts remain; new store capabilities and completion fields are additive.
- **Naming**: PASS — planned declarations remain within the five-component CamelCase cap.
- **Test discipline**: PASS — contract/safety tests precede implementation; Groundwork crash and race suites close provider semantics.
- **Constitution status**: The constitution remains draft/provisional. Accepted checkpoint, persistence, authentication, single-writer, and actor-boundary ADRs are controlling gates.

Post-design re-check: PASS. No constitution exception is required.

## Architecture and Flow

1. `DispatchWorkflowRuntimeFeature` snapshots a validated finite child-start `RuntimePostCommitRetryPolicy` from host feature options. The policy is persisted on each start outbox item; no activity schema changes.
2. `ChildStartExecutor` classifies explicit rejection as permanent infrastructure failure and deferred/unavailable delivery as transient. It removes provider reason/exception text from the durable result and never classifies a child terminal business fault as delivery failure.
3. The outbox processor claims with the existing owner/fencing/visibility contract. Transient failures below the configured total-attempt limit become `FailedRetryable` with positive delay. A permanent failure or exhausted transient failure becomes an effective `FailedFinal` result.
4. A DispatchWorkflow-owned final-failure projector validates the canonical start intent/dispatch, derives the deterministic delivery generation, incident ID, and fixed diagnostics, and returns a finalization aggregate. For wait mode it also builds one deterministic `DispatchFailed` parent-resume outbox item using the existing bookmark route; fire-and-forget builds no parent work.
5. In-memory and Groundwork completion stores check deterministic child-execution visibility inside the completion boundary. Durable child materialization or terminal evidence wins and the start item is acknowledged; otherwise they atomically persist the `FailedFinal` start item, the `DispatchFailed` dispatch/dead-letter metadata, and the optional pending parent-resume item. The original failed outbox item is the durable dead letter.
6. The parent-resume handler consumes the existing deterministic bookmark. `DispatchWorkflow` accepts `DispatchFailed`, publishes no outputs or raw details, emits the matching graph outcome, and completes normally. Wait-mode dead letters are permanently abandoned.
7. The safe inspection view reads incident/dead-letter ID, generation, attempt count, failure time, and eligibility from allowlisted dispatch metadata. Existing list/get routes remain runtime-read protected and tenant-scoped by the configured provider.
8. The runtime-manage redrive endpoint accepts only dispatch ID plus an operator request ID. A dedicated store capability loads every canonical identity and atomically validates fire-and-forget + `DispatchFailed` + matching `FailedFinal`, advances the generation/fence, reopens the same dispatch to Pending, and requeues the same outbox intent. Duplicate request IDs converge; other active/stale requests conflict.
9. Structured safe events cover attempt failure, retry schedule, final failure/incident, wait resume, and redrive result. They log stable IDs/classifications/times only.

## Project Structure

### Documentation

```text
specs/101-dispatch-delivery-recovery/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-delivery-recovery.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/                         # finalization and redrive capabilities
└── Models/                            # safe failure, generation, redrive, completion aggregate

src/Elsa/Workflows/Runtime/
└── Services/                          # processor, in-memory atomic finalization/redrive

src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Configuration/                     # host delivery policy
├── Models/                            # failure resume payload/result diagnostics
├── Services/                          # classification and final-failure projection
└── DispatchWorkflowRuntimeFeature.cs

src/Elsa/Persistence/Groundwork/Stores/
└── GroundworkRuntimePostCommitOutboxStore.cs

src/Elsa/Workflows/Runtime/Api/
├── Endpoints/WorkflowDispatchInspection.cs
├── Handlers/WorkflowDispatchInspectionRequestHandlers.cs
├── Models/WorkflowDispatchViews.cs
└── Requests/WorkflowDispatchInspectionRequests.cs

tests/Elsa/Activities/DispatchWorkflow/Tests/
tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Workflows/Runtime/Api/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend the existing runtime, activity, provider, and API projects. Add no parallel queue, broker, UI, or persistence subsystem.

## Ordered Delivery

1. Lock finite host policy, classified safe failure, effective final result, and diagnostic contracts with failing tests.
2. Extend the atomic final-claim aggregate and implement wait-mode failure resumption plus fire-and-forget dead-letter projection.
3. Implement in-memory and Groundwork atomic finalization, crash recovery, and stale-fence behavior.
4. Add explicit provider-atomic redrive and concurrency/idempotency tests.
5. Extend safe authenticated inspection, manage-protected redrive endpoint, capability discovery, and security tests.
6. Add structured safe observability and complete end-to-end wait/fire-and-forget/crash/duplicate verification.
7. Leave generated map snapshots unchanged unless the user explicitly invokes regeneration; run completion, safety, regression, and architecture audits.

## Complexity Tracking

No constitution violation requires justification. The additive final-failure projector keeps DispatchWorkflow payload construction out of Runtime Core while using the existing generic outbox completion boundary.
