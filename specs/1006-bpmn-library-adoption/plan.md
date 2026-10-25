# Implementation Plan: Compatible BPMN library adoption

**Branch**: `agent/codex/2735-bpmn-switch` | **Date**: 2026-10-25 | **Spec**: [spec.md](spec.md)

**Input**: #2735 part 2; [delivered parity](../../docs/reports/evidence/bpmn-adoption-0.5.0/README.md), frozen authoring baseline and the owner's continuation. Independent correctness, standards and acceptance reviews found the specification ready before this plan was written. No package or runtime switch has happened.

## Summary

Replace Foundation's duplicated semantics with released Bpmn.Semantics 0.5.0 behind the existing BpmnProcess authoring contract. First capture actual legacy runtime checkpoints with the old writer, including cancellation and deferred-fault boundaries that ordinary drain-to-idle tests cannot preserve. Then implement version-aware state I/O, exact durable work correlation, command application, combined outcomes and explicit work binding. Prove upgraded-host recovery and Studio behavior before removing the old semantics or calling the switch delivered.

This plan is execution authority within the owner's existing scope, not a claim that its proposed compatibility design has passed. The capture phase is a hard implementation gate: if evidence disproves a transition, revise/review that design while keeping the old writer.

## Technical Context

**Language/Version**: Existing C#/.NET 10 and Studio TypeScript/React; no framework upgrade.

**Primary Dependencies**: Published Bpmn.Model, Bpmn.Interchange and Bpmn.Semantics 0.5.0; existing Foundation runtime/design contracts. Runtime.InMemory is a library reference witness, not the Foundation execution host.

**Storage**: Existing checkpoint authority and providers. No second persistence authority or alternate replay ledger.

**Testing**: Whole BPMN runtime/interchange test projects, relevant runtime EF native-composite and combined-outcome projects, architecture/maps gates, Studio unit/browser tests and a real source-built Workbench journey.

**Target Platform**: Existing Foundation host and consolidated Studio.

**Project Type**: Existing optional activity/interchange packages and host integration.

**Performance Goals**: No new throughput or latency claim. Keep interpreter graph caching and released pruning; do not retain a second local token policy.

**Constraints**: Frozen activity fingerprint and Elsa authoring schema remain unchanged. Existing accepted state stays readable without silently changing its storage format. No Orleans, task-runner, direct-host completion or ADR acceptance in this PR.

**Scale/Scope**: One adoption issue/claim and one switch PR. Three implementation gates below, then exact-head author review and merge.

## Constitution Check

Both current constitutions and [review checklist](../../docs/contributing/review-checklist.md) apply. Current source has framework 6.1.0 and Elsa 6.3.0; the entrypoint's older Elsa version is not used to weaken newer gates.

| Gate | Application |
|---|---|
| Framework §2.1, §2.7 | Keep third-party types in the BPMN implementation/interchange boundary; no heavy dependency in a new Core. Map existing host contracts, do not create a universal execution API. |
| §2.5, §2.5.1, §2.11, §2.23.1 | Register new adapter/binder logic as scoped services unless an immutable instance has a specific singleton reason. Verify real importer/exporter/binder registration, not only resolvability of services that happen to exist. |
| §2.6.4; Elsa §E2.2/§E2.6 | Authoring/publish translation stays separate from runtime execution. A pinned published artifact executes without design-store lookup. |
| §2.21.1, §2.23.2 | Keep valid host assertions. Reconcile superseded semantics and moved pure tests against the 242-method ledger, with red/green controls for risky changes and deletion guards. |
| §2.23.3; Elsa §E6 | New implementation helpers internal sealed; keep public activity/feature/contracts only where consumers need them. Track intentional API deltas under the existing package policy. |
| §2.23.5 | Wrap library/JSON boundary failures in the appropriate domain failure with identifiers and original exception; do not turn corrupt state into a fresh execution. |
| §2.22 | Update affected BPMN/interchange READMEs, contribution/catalog entries only where their actual extension points change. Link canonical meanings. |
| §4.2; Elsa §E5 | Record external public API/package deltas separately from the frozen authoring contract. Respect the pre-GA version line. |
| Elsa §E7 | Reuse existing test projects where practical; new test projects need tier membership and provider-family admission. |
| Framework §2.24; Elsa §E2.8 activity versioning/§E2.9 | Draft/provisional sections are excluded from ratification; this plan does not ratify them or redesign their subjects. |

Pre-design: no exception is proposed. Post-design: the adapter and versioned I/O below preserve these boundaries; actual tests, source review and package/API comparison remain gates before merge.

## Project Structure

- `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/`: activity entrypoint, graph/host translation, command applier, state compatibility I/O and trigger/schedule providers.
- `Essentials/src/Activities/Bpmn/Interchange/`: registered work binder and authored/library translation.
- `Essentials/tests/Activities/Bpmn/Tests/`: actual legacy capture, compatibility and adapter/guard tests.
- `Essentials/tests/Activities/Bpmn/Interchange/Tests/`: binding and registered interchange proof.
- `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/`: fresh-provider recovery and native-parent witnesses.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/`: preserved authoring mirror/default-flow behavior and browser acceptance.
- `specs/1006-bpmn-library-adoption/`: reviewed artifacts, fixture provenance and acceptance receipts.

## Implementation gate A: freeze actual legacy checkpoints

Do not change package pins, delete state classes or rewrite the legacy persister until this gate passes.

1. Use the actual WorkflowExecutionHarness/host and current BpmnProcess. A test-only checkpoint-store decorator may pause immediately after a committed checkpoint, before the next scheduler/outbox action. Capture actual serialized documents and complete dependent state; do not manufacture typed tokens and call that an execution.
2. Capture a resolved race with a canceled loser and trailing wait; a separate explicitly controlled loser-completion-already-queued order; parallel MI work with different identities; a committed PendingFault generated by an evaluation that already staged work; and a non-interrupting event-subprocess listener with a terminal previous invocation and its live re-arm at the same node/null iteration. The re-arm witness must reject first-node/iteration matching under mutation.
3. Retain source revision, package pins, executable, workflow/parent/child documents, bookmarks, value/iteration scope, incidents, scheduler queue and outbox records. Assert the state preconditions in the capture test. A gate must fail when its required order or actual PendingFault was not reached.
4. Reload a captured checkpoint with the old host as a control. This validates capture fidelity; it does not qualify the upgraded host.
5. Commit fixtures and provenance. Any temporary capture switch is opt-in and cannot overwrite committed evidence during ordinary tests.

## Implementation gate B: compatible host boundary

### Definition and binding translation

Keep Elsa's existing authored DTO/envelope and serialized field names. Map them to neutral BpmnProcessDefinition/BpmnBoundWork for graph construction; obtain nested definitions from the Bpmn.Activities child slot. Triggers and recurring schedules use the same graph translation. Avoid importing library schemas into the Studio public envelope.

Extract the existing MaterializeChild policy into a named binder. Preserve Delay/Event/PublishEvent/DispatchWorkflow/nested-process mappings, CanStartWorkflow=false on nested/listener work, called-workflow policy and placeholder resolution through the real service graph. New descriptors without a proved host implementation must produce explicit degradation/unavailability; a null child cannot silently mean available work. Derive the default-flow marker from the source element's DefaultFlowId, not the library flow's IsDefault flag.

### Versioned state compatibility

Use one state I/O component with explicit readers/writers for legacy alias/version 1 and the new library-backed format. The old wire field names activeChildren and producingActivityExecutionId need deliberate translation; preserve every other race/loop/compensation/diagnostic/deferred-fault field. Do not retain duplicate typed semantic state classes merely to rename fields; a versioned JSON/DTO boundary can map to library state.

Existing version-1 executions retain their version-1 storage representation on continuation; new executions may use a separately versioned library representation. Reading v1 is not permission to restamp it v2. A v1 writer projection must retain the legacy names while carrying any additive exact stopped-handle data required for continuation. Read-back with the old reader is a control, not sufficient rollback qualification. Document the actual old/new reader matrix after the captured fixtures decide it.

Resolve active/stopped work from all durable direct children (terminal included), exact callback child lookup and queued scheduling provenance, corroborating parent, element/node, token ExecutionPathId and iteration. No arbitrary match or scope invocation correlation. Distinguish a never-issued/withdrawn token from work whose identity is merely missing. If correlation cannot be established, preserve the source checkpoint and refuse dispatch with a compatibility fault. Do not prune before the required correlation is restored. This is a readability/correlation boundary, not a copied semantic retention algorithm.

Invocation correlation is scope-fixed and persisted separately from callback identity; recover only documented prior fields. A newly supplied correlation dictionary never substitutes for historical evidence.

### Interpreter and command application

Provide an immutable explicit capability set and host value reader. For each callback build exact live-work identities with WorkTokenId. Terminal callbacks exclude their terminal handle. StartWork records token correlation and ProducedByHandle provenance, iteration frames and scheduling cause. Commands map to ScheduleChildActivity, RequestChildSubtreeCancellation and RequestParentNotification, then the library verdict and state stage through one Foundation continuation.

BpmnProcess implements the existing combined outcome handler. Fold ordered notifications against successive library states locally; for those notification evaluations the exact reporting work is semantically live even though its fault checkpoint is already committed. Remove it before the terminal fault evaluation. Remove virtually canceled handles between evaluations. When an earlier notification emits a teardown for the exact reporting handle, retire that handle in the local delivery ledger and consume subsequent notifications for that retired invocation without calling OnWorkSignalled again. Do not omit WorkTokenId or accidentally target another live invocation at the same binding. This follows released Runtime.InMemory RunWorkSignalled, which drops queued signals after their handle retires; it is host delivery policy, not a new escalation matcher. Still report the original terminal fault with its exact handle/token so the interpreter can return Absorbed from the stopped-handle record. Do not prune between notifications and the fault. The final fault disposition Caught or Absorbed, together with a non-fault continuation, permits absorption of only the original incident.

Accumulate ordered command results across this one parent evaluation. If the batch finishes with Fault, suppress every unissued StartWork and runtime-inadmissible subtree cancellation, including those accumulated by earlier notification evaluations; final active-work filtering alone is insufficient because the interpreter withdraws only starts from its current call. Preserve outward signals and the first semantic terminal verdict, while retaining the original child fault/incident as evidence. On a non-fault result, reconcile unissued starts against final active-work/token identities and coalesce repeated teardown requests. Prove the non-interrupting-handler-start followed by uncaught fault case, two notifications with first interruption, and a concurrent same-node invocation that must remain untouched. A teardown for the already-terminal reporting child must not ask the runtime to cancel a non-live child. These are command/snapshot adaptations; all token routing and fault matching remain in the interpreter. Prove interrupting and non-interrupting cases through the actual runtime before accepting this design.

Map BPMN error codes separately from incident/fault classification. Preserve the exact withheld-variable refusal. Never rethrow a raw library exception across the host boundary or replace an original fault with a guessed code.

### Deletion gate

Only after gate A plus actual upgraded-reader/adapter tests pass, remove BpmnExecutionEngine, BpmnGraph, coordinator/mutator/registry/behaviors and duplicated semantic state. Remove PruneForPersistence; use library Prune after correlation restoration. Preserve host-owned authored DTOs, BpmnDecision, structure handlers and trigger/binding logic.

The guard covers public Bpmn.Semantics simple-name collisions plus an explicit deleted-semantics deny-list. Do not blanket-ban Bpmn.Model names that are intentionally retained authored compatibility DTOs; document that distinction and prove the guard catches an actual reintroduced engine/graph. Pure semantic tests may move only with a named released replacement; host behavior remains exercised through Foundation.

## Implementation gate C: integration and qualification

- Reopen actual legacy fixtures through a fresh upgraded host/provider and finish them. Include late faults/completions, already-queued order, duplicate outcomes, retained original fault/incident, pruning and rollback-reader limits.
- Repeat Sequence and outer Flowchart witnesses from spec242 on the new adapter in both orders; preserve parent-owned joins, cancellation and existing fault-status differences.
- Exercise actual registered import/export and real Workbench/Studio import, default-edge editing, publish, execution and export. Include nested scopes, ordinary compensation and interrupting/non-interrupting Call Activity escalation.
- Compare actual published descriptors with the frozen fingerprint. Keep historical baseline files untouched; reconcile test changes in an evidence ledger.
- Run affected whole projects and compare discovery/executed counts; verify meaningful red/green controls, architecture guards, locked restore when references change, generated maps and relevant Studio tests.
- Open only this second ordered draft PR on #2735, review to convergence, retain required checks/protection and verify resulting main. Finish spec status in the implementing PR. ADR acceptance follows in PR3.

## Review and evidence

Every gate reports actual source/head, commands, fixtures, failures/skips and limits on #2735 and the PR. No synthetic state, library-only test or same-provider baseline is restamped as upgraded-host or Studio acceptance. If a fixture disproves the proposed reader or combined-outcome mapping, correct and review the design before deleting the legacy writer.

Optional Spec Kit auto-commit hooks are handled by explicit reviewed commits; disabled agent-context hooks stay disabled. No unrelated AGENTS update is generated. Independent plan review must complete before tasks are generated.

## Legacy capture implementation checkpoint

The committed capture path uses EF SQLite transaction interception and the provider's online database backup, preserving every coupled table, checkpoint replay marker, queue/outbox claim and bookmark at the actual boundary. Old-host controls consume a separate immutable-copy database through a fresh service provider. The earlier in-memory JSON probe was superseded by this whole-store witness and is not retained as a second capture framework.

The queued-loser fixture records distinct first queue-presence and first cancellation observations, requiring the queue observation to be earlier. A physical delayed-insert control creates the opposite durable order and must not capture. Removing the ordering guard made that control fail; restoring it passes. This establishes ordering sensitivity, not upgraded-host recovery. Actual portable artifacts still require the exact capture source revision, digests and review before gate A closes.
