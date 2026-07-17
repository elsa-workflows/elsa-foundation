# Implementation Plan: Wait for a Successful Child and Return Safe Outputs

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #679 and the Approved specification in this work unit.

## Summary

Add the successful wait-for-completion path without changing the stable activity surface. DispatchWorkflow stages a deterministic non-expiring bookmark with its existing dispatch/start responsibility, and the activity invoke handler commits that wait atomically instead of using the ordinary two-step bookmark queue. A child Completed checkpoint projects safe durable workflow outputs and records one deterministic parent-resume intent in the same commit. The global outbox handler reuses the existing bookmark resume dispatcher and retains its claim with positive backoff until authoritative state proves consumption or a terminal parent, emitting one payload-safe structured operational signal for every recorded retryable attempt. The ordinary bookmark-consumption checkpoint completes the activity with a structured safe result and `Completed` graph outcome. Groundwork tests recreate services across every parent, child, resume, output, and acknowledgement boundary.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Activities Runtime/Core, Workflows Runtime/Core/Resumption, DispatchWorkflow Runtime, Persistence Groundwork, Microsoft.Extensions.DependencyInjection

**Storage**: Existing runtime checkpoint transaction, bookmark/dispatch/durable-value documents, post-commit outbox v3, and additive outbox lookup capability

**Testing**: xUnit with runtime activity harnesses, in-memory checkpoint/outbox tests, Groundwork transaction/restart fixtures, resumption tests, and architecture guards

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project runtime feature with provider-backed continuation persistence

**Performance Goals**: One bounded dispatch lookup and one indexed outbox lookup per child terminal checkpoint; one output-state read per successful child; no polling inside workflow actor mailboxes

**Constraints**: Atomic parent wait; exact retained artifact; deterministic resume; safe typed/redacted outputs; unbounded backoff only for parent-resume kind; alertable retry telemetry must exclude payload/result/exception values; no timeout, broker, fault/cancel propagation, dead-letter/redrive, TestRun, or distributed-placement expansion

**Scale/Scope**: One child per DispatchWorkflow activity execution and one parent-resume intent per successful waited child

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Runtime/Design boundary**: PASS — all work is runtime state over already pinned artifacts. No Design dependency is introduced.
- **Artifact-only execution**: PASS — child start reuses #677’s exact retained pin; resume uses durable parent/child identities and no authored definition lookup.
- **Checkpoint/post-commit rule**: PASS — parent wait responsibility and child terminal resume responsibility each commit before cross-execution delivery.
- **Single-writer/actor boundary**: PASS — child completion records outbox work but does not mutate the parent. Parent mutation occurs through the existing bookmark resume actor path.
- **Contribution semantics**: PASS — intent kind and delivery policy share the existing conflict-safe handler contribution; no keyed domain switch is added to the dispatcher.
- **Provider neutrality**: PASS — atomic change sets and additive store contracts live in Runtime Core; Groundwork adapts them through the existing document unit-of-work.
- **Safety**: PASS — output projection reuses the configured payload capture policy, retains redaction markers, and never serializes redacted values.
- **Replay/idempotency**: PASS — deterministic identities plus committed-outbox lookup preserve exact terminal intent payload across uncertain acknowledgement and configuration change.
- **Compatibility**: PASS — base stores, public constructors, fire-and-forget behavior, handler vocabulary, and unsupported-kind policy remain intact.
- **Test discipline**: PASS — focused unit tests precede implementation; provider tests cover every authoritative crash boundary and full regression suites close the unit.
- **Constitution status**: The constitution remains draft/provisional. Accepted checkpoint, artifact, persistence, and single-writer ADRs are controlling gates.

Post-design re-check: PASS. No constitution exception is required.

## Architecture and Flow

1. `DispatchWorkflow` validates its retained pin and child inputs as today. In wait mode it creates the deterministic wait bookmark, stages a wait-mode dispatch request, and does not select an outcome yet.
2. `WorkflowInvokeActivitySchedulerWorkHandler` recognizes the matching staged wait and directly builds one mandatory bookmark-created commit containing Suspended activity state, bookmark, Pending dispatch, child-start intent, inspection, and write-back. It notifies bookmark observers only after commit.
3. #678’s outbox claim and deterministic start path materializes the child and advances dispatch to Started.
4. On a child Completed checkpoint, the runtime lifecycle enricher appends dispatch Completed. The DispatchWorkflow completion enricher derives the resume outbox ID, reuses an existing committed intent on replay, or reads and safely projects child workflow outputs before appending the deterministic resume intent.
5. The provider commits child Completed state, dispatch Completed, safe parent-resume outbox, and marker in one transaction.
6. The global resumption pump claims the parent-resume item. `ParentResumeExecutor` validates it and calls the existing bookmark resume dispatcher with a deterministic idempotency key.
7. The handler rechecks bookmark/activity/workflow state. If still waiting it records a retryable attempt with positive backoff and no exhaustion. The outbox processor emits a structured warning with stable work identifiers, intent kind, saturated attempt count, and next availability only. Once consumed or terminal, it acknowledges.
8. The existing bookmark-consumption path invokes DispatchWorkflow’s resume target, sets the safe result and `Completed`, deletes the bookmark, completes the activity, and records ordinary graph propagation atomically.

## Project Structure

### Documentation

```text
specs/099-dispatch-wait-success/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-wait-success.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/                         # additive output source and outbox lookup
└── Models/                            # identity, staging, retry-policy/contribution compatibility

src/Elsa/Workflows/Runtime/
├── Services/RuntimePostCommitOutboxItems.cs
├── Services/RuntimePostCommitOutboxProcessor.cs
├── Services/RuntimeWorkflowOutputStateProjection.cs
└── Extensions/RuntimeCoreServiceCollectionExtensions.cs

src/Elsa/Activities/Runtime/Services/
└── WorkflowInvokeActivitySchedulerWorkHandler.cs

src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Activities/DispatchWorkflow.cs
├── Constants/DispatchWorkflowConstants.cs
├── Models/WorkflowDispatchParentResumePayload.cs
├── Services/WorkflowDispatchCompletionEnricher.cs
├── Services/ParentResumeExecutor.cs
└── DispatchWorkflowRuntimeFeature.cs

src/Elsa/Persistence/Groundwork/
├── Serialization/                    # current-only outbox v3 baseline
└── Stores/GroundworkRuntimePostCommitOutboxStore.cs

tests/Elsa/Activities/DispatchWorkflow/Tests/
tests/Elsa/Activities/Runtime/Tests/
tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend existing runtime, DispatchWorkflow, activity engine, and Groundwork projects. Add no broker, parallel resume stack, or new persistence project.

## Ordered Delivery

1. Lock public compatibility, identity, lookup, and retry-policy behavior with failing tests.
2. Add the atomic wait checkpoint and prove no child visibility before commit.
3. Add safe Completed checkpoint enrichment with exact replay reuse.
4. Add parent-resume delivery, consumption-aware retry, payload-safe alertable retry signals, and resume callback result.
5. Replace the pre-GA Groundwork outbox baseline with current-only v3 and add the complete crash/restart matrix.
6. Update extension-point docs/maps, run full regression/architecture audits, and re-audit the Approved spec against implementation evidence.

## Complexity Tracking

No constitution violation requires justification.
