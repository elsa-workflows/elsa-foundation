# Implementation Plan: Complete Child Fault and Cancellation Semantics

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #680 and the Approved specification in this work unit.

## Summary

Extend #679's deterministic terminal-resume path to Faulted and Cancelled children with zero partial outputs and a strict diagnostic allowlist. Persist the wait-only cancellation policy on each dispatch. Enrich a parent cancellation checkpoint with one replay-stable provider-resolved cancellation directive and one deterministic child-cancel intent. Built-in providers resolve the directive inside the same transaction: Pending becomes cancelled-before-admission, Started records cancellation responsibility, and terminal state wins unchanged. Child start first performs an atomic admission transition, so admission or cancellation wins one durable race. A contributed unbounded-retry handler delivers deterministic Cancel commands through the configured actor provider until authoritative child state acknowledges the responsibility.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Activities Runtime/Core, Workflows Runtime/Core/Resumption, DispatchWorkflow Runtime, Persistence Groundwork, Microsoft.Extensions.DependencyInjection

**Storage**: Existing runtime checkpoint transaction and post-commit outbox plus an additive dispatch cancellation directive and conditional admission capability

**Testing**: xUnit runtime/activity tests, provider transaction and restart fixtures, race/duplicate tests, and architecture guards

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project runtime feature with provider-backed continuation persistence

**Performance Goals**: Bounded parent dispatch query; one conditional dispatch write per start; one cancellation mutation per eligible child; no workflow-mailbox polling

**Constraints**: Normal graph outcomes; zero partial outputs; fixed safe diagnostics only; wait-only default propagation; provider-atomic admission/cancellation; deterministic actor commands; no #681 exhaustion/redrive, #682 TestRun, #683 distributed transport, broker, Studio, or WorkflowDefinitionActivity expansion

**Scale/Scope**: One deterministic child per DispatchWorkflow activity; bounded paging for all waited children owned by one cancelling parent

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Runtime/Design boundary**: PASS — behavior uses retained runtime artifacts and existing activity schema only.
- **Artifact-only execution**: PASS — no child definition resolution or authored mutable target is introduced.
- **Checkpoint/post-commit rule**: PASS — terminal resume and parent cancellation responsibility commit before cross-execution delivery.
- **Single-writer/actor boundary**: PASS — providers mutate dispatch coordination state; child workflow state changes only through its actor Cancel command.
- **Contribution semantics**: PASS — child-cancel delivery is one conflict-safe intent-kind contribution.
- **Provider neutrality**: PASS — cancellation directives and admission results live in Runtime Core; built-in providers adapt them.
- **Safety**: PASS — fault results expose only fixed classification/summary and stable incident IDs; cancellation exposes no child data.
- **Replay/idempotency**: PASS — query-independent directives and deterministic intent/command identities preserve fingerprints and at-least-once convergence.
- **Compatibility**: PASS — existing activity inputs/outcomes, constructors, public base stores, Completed behavior, and fire-and-forget semantics remain available.
- **Naming**: PASS — planned declarations remain within the five-component CamelCase cap.
- **Test discipline**: PASS — race and safety tests precede implementation; Groundwork recreation closes provider claims.
- **Constitution status**: The constitution remains draft/provisional. Accepted checkpoint, persistence, single-writer, and actor-boundary ADRs are controlling gates.

Post-design re-check: PASS. No constitution exception is required.

## Architecture and Flow

1. DispatchWorkflow computes the effective propagation policy as `wait && authored/default true` and stores its invariant value in dispatch metadata.
2. Child-start delivery atomically admits Pending to Started through an additive store capability before calling the existing workflow start dispatcher. A pre-admission Cancelled marker makes delivery an acknowledged no-op; an already Started delivery repeats the deterministic start safely.
3. A parent Cancel checkpoint is enriched with a canonical cancellation directive and deterministic child-cancel intent for each nonterminal wait-mode propagation-enabled dispatch.
4. The provider resolves each directive inside the checkpoint transaction. Pending becomes Cancelled with a sanctioned before-admission marker; Started keeps its status and gains a cancellation-requested marker; terminal records remain unchanged. The outbox intent and parent Cancelled state commit in the same unit.
5. Child-cancel delivery validates deterministic identity and the dispatch. It acknowledges a suppressed start or terminal child, retries while an admitted child is not visible, and otherwise enqueues one deterministic at-least-once Cancel command through the configured actor provider and partition.
6. A child terminal checkpoint projects Completed/Faulted/Cancelled dispatch state. The completion enricher reuses committed terminal intent on replay; only Completed reads outputs, only Faulted reads stable incident IDs, and Cancelled reads neither.
7. The existing parent-resume route consumes the exact bookmark. DispatchWorkflow maps the safe terminal result to Completed/Faulted/Cancelled and completes normally. A cancelled parent is already terminal, so delivery acknowledges without resuming it.

## Project Structure

### Documentation

```text
specs/100-dispatch-fault-cancellation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-fault-cancellation.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/                         # additive admission/cancellation capabilities
└── Models/                            # directive, result, identity, lifecycle/fingerprint

src/Elsa/Workflows/Runtime/
└── Services/                          # in-memory checkpoint/store atomic application

src/Elsa/Activities/DispatchWorkflow/Runtime/
├── Activities/DispatchWorkflow.cs
├── Constants/DispatchWorkflowConstants.cs
├── Models/WorkflowDispatchChildCancelPayload.cs
├── Models/WorkflowDispatchParentResumePayload.cs
├── Services/ChildStartExecutor.cs
├── Services/ChildCancelExecutor.cs
├── Services/WorkflowDispatchCancellationEnricher.cs
├── Services/WorkflowDispatchCompletionEnricher.cs
└── DispatchWorkflowRuntimeFeature.cs

src/Elsa/Persistence/Groundwork/Stores/
├── GroundworkRuntimeCheckpointWriter.cs
└── GroundworkWorkflowDispatchStore.cs

tests/Elsa/Activities/DispatchWorkflow/Tests/
tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend the existing runtime, DispatchWorkflow, and Groundwork projects. Add no parallel command bus or persistence unit.

## Ordered Delivery

1. Lock safe terminal-result, policy metadata, identity, directive, and admission contracts with failing tests.
2. Add provider-neutral directive/fingerprint and in-memory admission/cancellation behavior.
3. Add Groundwork transactional directive resolution and admission CAS with restart/race tests.
4. Add parent cancellation enrichment and deterministic child Cancel delivery.
5. Extend terminal resume/result/outcome behavior and safe fault diagnostics.
6. Run duplicate/terminal race, full regression, map, extension-point, and completion audits.

## Complexity Tracking

No constitution violation requires justification. The new cancellation directive collection is necessary because a query-derived concrete lifecycle update is replay-unstable and cannot close the admission race.
