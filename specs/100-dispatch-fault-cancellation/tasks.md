# Tasks: Complete Child Fault and Cancellation Semantics

**Input**: Approved specification and plan in `specs/100-dispatch-fault-cancellation/`

**Tests**: Required. Every behavioral phase begins with failing focused tests and closes with proportional regression/provider evidence.

## Phase 1: Contract and test scaffolding

- [x] T001 Confirm `.specify/feature.json` selects `specs/100-dispatch-fault-cancellation` and the worktree remains on `codex/dispatch-workflow-program`.
- [x] T002 Add terminal-result and policy-default contract tests in `tests/Elsa/Activities/DispatchWorkflow/Tests/DispatchWorkflowContractTests.cs`.
- [x] T003 Add deterministic child-cancel identity tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowDispatchIdentityTests.cs`.
- [x] T004 Add lifecycle metadata and cancellation-request model tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowDispatchDurabilityContractTests.cs`.
- [x] T005 Add admission/cancellation capability test doubles and shared fixtures without duplicating arrange blocks.

## Phase 2: Provider-neutral coordination model

- [x] T006 Add effective-policy and sanctioned cancellation marker constants/helpers in Runtime Core / DispatchWorkflow constants.
- [x] T007 Add deterministic child-cancel intent, idempotency, command, and envelope identities to `WorkflowDispatchIdentity`.
- [x] T008 Add `WorkflowDispatchCancellationRequest` with deterministic identity and timestamp validation.
- [x] T009 Add additive admission result and `IWorkflowDispatchAdmissionStore` contract.
- [x] T010 Add additive `IWorkflowDispatchCancellationStore` contract for provider-resolved checkpoint requests.
- [x] T011 Extend lifecycle rules to create/validate before-admission and cancellation-requested markers while preserving immutable policy metadata.
- [x] T012 Extend `RuntimeCheckpointStateChangeSet` with cancellation requests while preserving every existing constructor overload.
- [x] T013 Include ordered cancellation requests in checkpoint fingerprints and reject duplicate conflicting requests.
- [x] T014 Carry cancellation requests through coalescing state/session adapters; cancel checkpoints remain mandatory flush.
- [x] T015 Run Runtime Core/model/coalescing focused tests.

## Phase 3: Built-in provider atomicity

- [x] T016 Add failing in-memory tests for admission-wins, cancellation-wins, duplicate admission, terminal precedence, and policy markers.
- [x] T017 Implement atomic admission in `InMemoryWorkflowDispatchStore` under shared state locking.
- [x] T018 Implement provider-resolved cancellation request application in the in-memory store/checkpoint commit path.
- [x] T019 Prove the in-memory checkpoint atomically records parent Cancelled state, directive result, and child-cancel outbox.
- [x] T020 Add failing Groundwork dispatch-store tests for optimistic admission and cancellation resolution.
- [x] T021 Implement Groundwork atomic admission with document-version fencing.
- [x] T022 Apply cancellation requests inside `GroundworkRuntimeCheckpointWriter`'s existing cross-unit transaction.
- [x] T023 Prove barrier-forced admission-wins and cancellation-wins orders plus at least 100 concurrent repetitions converge without lifecycle regression or fingerprint drift.
- [x] T024 Confirm v1 workflow-dispatch golden fixtures remain source/schema compatible without an upcaster or schema bump and that legacy effective policy resolves by mode.
- [x] T025 Run focused in-memory and Groundwork provider suites.

## Phase 4: Parent cancellation and child Cancel delivery

- [x] T026 Add policy persistence tests for default true, explicit false, absent runtime input, legacy wait metadata, and fire-and-forget effective false.
- [x] T027 Persist canonical effective cancellation policy during DispatchWorkflow staging.
- [x] T028 Add `WorkflowDispatchCancellationEnricherTests` for Cancel-only trigger, Pending/Started/terminal, opt-out, fire-and-forget, paging, ordering, and replay.
- [x] T029 Implement `WorkflowDispatchCancellationEnricher` producing canonical requests and deterministic intents in the parent checkpoint.
- [x] T030 Add validated `WorkflowDispatchChildCancelPayload` with stable identifiers only.
- [x] T031 Add `ChildCancelExecutorTests` for suppressed start, missing admitted child, deterministic actor envelope, accepted/duplicate/rejected/deferred, and terminal precedence.
- [x] T032 Implement `ChildCancelExecutor` over `IWorkflowExecutionActorProvider` and authoritative child/dispatch state.
- [x] T033 Register child-cancel handler with positive-backoff `RetryUntilAcknowledged`, register the cancellation enricher in deterministic order, and make readiness initialization reject stores missing either additive capability.
- [x] T034 Extend `ChildStartExecutorTests` for pre-start admission, cancellation suppression, already-admitted replay, terminal no-op, opt-out, and fire-and-forget compatibility.
- [x] T035 Move child admission before external start in `ChildStartExecutor`; fail closed when required capability/state is absent.
- [x] T036 Run DispatchWorkflow cancellation/actor focused tests.

## Phase 5: Faulted and Cancelled parent results

- [x] T037 Add completion-enricher tests for Faulted/Cancelled intent creation, exact replay, zero output reads, sorted incident IDs, and forbidden-data absence.
- [x] T038 Extend terminal completion enrichment to Completed/Faulted/Cancelled and inject bounded incident lookup for Faulted only.
- [x] T039 Implement exact fixed diagnostic literals, invariant count/truncation fields, ordinal dedupe/sort, and a 32-incident cap; reject caller-supplied unsafe fields and test overflow serialization.
- [x] T040 Generalize `WorkflowDispatchParentResumePayload` validation to exactly the three supported child terminal statuses.
- [x] T041 Extend activity resume tests for matching Completed/Faulted/Cancelled results and rejection of all other/mismatched payloads.
- [x] T042 Map the terminal result to one ordinary matching graph outcome without throwing the activity.
- [x] T043 Add unconnected-outcome integration tests proving zero implicit escalation or parent incident.
- [x] T044 Run DispatchWorkflow terminal-result and activity runtime suites.

## Phase 6: Race, restart, compatibility, and completion audit

- [x] T045 Add parent cancellation composition coverage to `RuntimeCancellationContractTests.cs`.
- [x] T046 Add Groundwork restart cases before/after admission, cancellation directive commit, child visibility, Cancel enqueue, terminal notification, claim expiry, and uncertain acknowledgement.
- [x] T047 Add three-delivery duplicate matrices for start, cancel, terminal notification, parent resume, and parent cancellation in both race orders.
- [x] T048 Prove opt-out and fire-and-forget independence across in-memory and Groundwork paths.
- [x] T049 Run Runtime, DispatchWorkflow, Activities Runtime, Resumption, Groundwork, Publishing/API, and Architecture regression suites with `/usr/local/share/dotnet/dotnet`.
- [x] T050 Update Runtime and Groundwork `EXTENSION_POINTS.md` catalogs for admission/cancellation capabilities and handlers.
- [x] T051 Refresh the narrow relevant maps and review generated findings for drift.
- [x] T052 Audit naming: every added/renamed CamelCase declaration has at most five components.
- [x] T053 Run final Speckit cross-artifact and issue-acceptance analysis; remediate every CRITICAL/HIGH/MEDIUM finding.
- [x] T054 Mark tasks complete only with evidence, verify the worktree diff is scoped, and create the required local #680 commit.

## Dependencies

- T001–T005 establish the failing contract surface.
- T006–T015 must complete before either provider implementation.
- T016–T025 establish the atomic race primitive before delivery code.
- T026–T036 implement cancellation responsibility on that primitive.
- T037–T044 can proceed after the shared terminal model and integrates before end-to-end races.
- T045–T054 close provider recovery, compatibility, documentation, and completion evidence.

## Parallel Opportunities

- After T015, in-memory (T016–T019) and Groundwork (T020–T024) provider work may be explored independently, but root integration owns the shared contract.
- After T025, cancellation delivery (T026–T036) and terminal results (T037–T044) may be implemented independently in non-overlapping files.
- Final race tests and audits remain root-owned integration work.
