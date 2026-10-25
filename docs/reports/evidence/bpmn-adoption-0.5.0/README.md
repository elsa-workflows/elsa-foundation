# BPMN 0.5.0 adoption: parity and host boundaries

Point-in-time parity deliverable for [#2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735),
25 October 2026. This is the first of its ordered parity/upstreaming, switch and ADR PRs.
The [owner handoff](https://github.com/elsa-workflows/elsa-foundation/issues/2735#issuecomment-6396983746)
selects Codex to deliver the stack. [ADR 0063](../../../adr/0063-bpmn-moves-to-a-host-agnostic-library.md)
continues to govern what leaves and what stays; it is not Accepted by this report.

## Compared sources and result

| Input | Exact revision / meaning |
|---|---|
| Foundation baseline | `98efed4fc2c07337ad0b30128b60f462b8ddcff0`; embedded engine, Model/Interchange `0.1.1-preview.19` |
| Selected parity target | Released BPMN **0.5.0**, source `021d2c5bba494f17d346144d97763cd3b2a13a4b`, library payload **1.10.0** |
| Release qualification | [Permanent artifact receipt](https://github.com/valence-works/bpmn/releases/download/v0.5.0/bpmn-0.5.0-qualification.json), [release evidence](https://github.com/valence-works/bpmn/pull/99#issuecomment-6393844989); package/artifact qualification, not Foundation adoption |
| Existing interpreter audit | 242 Foundation methods: 197 ported, 23 already covered, 19 host-only, 3 intentionally changed; [part 1](https://github.com/valence-works/bpmn/issues/38#issuecomment-6392471534), [2](https://github.com/valence-works/bpmn/issues/38#issuecomment-6392471747), [3](https://github.com/valence-works/bpmn/issues/38#issuecomment-6392471977), [4](https://github.com/valence-works/bpmn/issues/38#issuecomment-6392472266) |

No Foundation-only interpreter behavior needing a new upstream implementation was identified in
this comparison. The released library includes the neutral scenario port and subsequent fixes.
The adoption work is the real Elsa host adapter, wire/state compatibility and integrated proof;
passing library tests cannot discharge those obligations. No package pin or engine has changed here.

The Foundation engine source is unchanged from audit source
`e4a48e3bdfd8a7cf58a83fd4d5c760e01b93baad`. Every current runtime test's **class plus method** identity
matches a row of that audit: 242 matches, no missing or orphan methods. Simple method names alone
would collapse two cross-class duplicates. The audit links are historical evidence; their neutral
test anchors were also checked against the released target, separately from assertion equivalence:
221 of 222 unique anchors remain by name. The remaining escalation-host validation anchor was
renamed to
[`A_task_cannot_host_an_escalation_boundary`](https://github.com/valence-works/bpmn/blob/021d2c5bba494f17d346144d97763cd3b2a13a4b/tests/Bpmn.Semantics.Tests/EscalationValidationTests.cs#L48).
It still rejects the ordinary Task fixture while allowing Call Activity, which has a separate
[imported composition test](https://github.com/valence-works/bpmn/blob/021d2c5bba494f17d346144d97763cd3b2a13a4b/tests/Bpmn.Conformance.Tests/HostWorkCompositionTests.cs#L26).
Do not interpret the historical subprocess-only test name as the released profile.

## Element and validation comparison

Foundation sources are under
[`Elsa.Activities.Bpmn`](https://github.com/elsa-workflows/elsa-foundation/blob/98efed4fc2c07337ad0b30128b60f462b8ddcff0/Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/);
released neutral sources are
[`Bpmn.Semantics`](https://github.com/valence-works/bpmn/tree/021d2c5bba494f17d346144d97763cd3b2a13a4b/src/Bpmn.Semantics).
The family resolvers, graph validators, behaviors and interpreter coordinators were compared as
well as the method inventory. “Covered” below describes the neutral mechanism, not Elsa execution.

| Family / boundary | Foundation baseline | Released target and switch obligation |
|---|---|---|
| Task family, subprocess and call activity | Bound Elsa children; nested structural processes | Neutral work model covers these; preserve exact invocation, outcome and variable mappings in the host |
| Exclusive, parallel and inclusive gateways | Outcome/default routing, forks/joins | Covered; preserve parent-independent token accounting and host decision outcomes |
| Cyclic flows and multi-instance | Backward-edge iteration scope; sequential/parallel cardinality and collection snapshots | Covered; retain exact refused-variable code/message and distinct token/iteration/child identities |
| Event-based gateway | First catch resolves the race and cancels losers | Covered; real bookmark cleanup, already-queued callbacks and restart remain host tests |
| Start events | None/message/signal/timer plus event-subprocess starts | Interpreter covered; publish-time trigger selection and durable scheduling remain Elsa-owned |
| Catch/throw/end events | Duration/message/signal catches; compensation/escalation/message throws; terminate/cancel ends | Library additionally supports link jumps, bound signal publication and BPMN error ends; represent these deliberately in the adapter/profile |
| Error boundaries and event subprocesses | Single any-error catcher; Elsa fault incident | Library supports distinct BPMN error codes plus a catch-all; do not conflate an incident/fault code with a BPMN error code |
| Escalation | Exact/catch-all matching; interrupting/non-interrupting subprocess boundary | Library also admits Call Activity hosts; prove notification forwarding and teardown through real calls |
| Compensation and transactions | Reverse/targeted replay, cancel-end outcome and cancellation boundary | Covered; preserve ordinary host-bound task handlers, replay identity and incident disposition |
| Termination and canceled work | Canceled tokens retained indefinitely for late callbacks | Library tracks an exact stopped-work handle, absorbs its late callback once, then permits pruning; migrate correlation before using that policy |
| Graph validation | Scope, references, roles, joins, loops, handlers and child bindings | Common mechanisms covered; new coded-error/event-family validation supersedes narrower historical expectations |

Three old expectations are intentionally superseded, not silently dropped: terminate now tears
down live race work; a valid cycle-backed cron event subprocess is supported while the old cron-only
fixture remains invalid; a bound signal throw is supported while missing broadcast work remains
invalid. The audit names the corresponding tests. Retain their failure scenarios while updating
obsolete expectations under current [test discipline](../../../../.specify/memory/constitution-framework.md#221-test-discipline).

The [cut disposition ledger](cut-ledger.md) separately accounts for the explicit runtime and
interchange limitations: library-supported host gaps, retained neutral cuts, host policy, and
aligned validation/profile restrictions. It checks active source predicates where README wording
is stale. No gap conclusion above depends on treating an omitted cut as supported.

## Host adapter and private-state gates

**Pruning ownership (issue step 2).** The old Elsa `PruneForPersistence` policy must leave with
its interpreter; `BpmnStatePersister` keeps loading/staging, not token-retention rules. Released
0.5.0 already provides
[`BpmnExecutionState.Prune()`](https://github.com/valence-works/bpmn/blob/021d2c5bba494f17d346144d97763cd3b2a13a4b/src/Bpmn.Model/State/BpmnExecutionState.cs#L88)
in `Bpmn.Model`, the state dependency of `Bpmn.Semantics`. No neutral gap requires a second pruning
implementation. The switch uses that library policy after restoring legacy correlation, then
persists its result. Delete Elsa's old `PruneForPersistence` and verify the late-callback behavior;
do not silently retain a host-side copy or move the same policy into a new adapter helper.

The adapter must apply library commands in order, stage state and owed work through the existing
checkpoint authority, and map the library verdict rather than reconstruct token semantics.
`StartWork`, `CancelWorkSubtree` and `SignalEnclosingScope` map to the existing scheduler,
subtree-cancellation and parent-notification operations; exact fault disposition governs incident
resolution. Signals raised before a fault must survive as one combined outcome under
[spec 126](../../../../specs/126-runtime-child-parent-notification/spec.md).
`BpmnProcess` currently implements separate completion/fault/notification callbacks, so it still
needs the combined-outcome adapter and durable proof. Native parent ordering/joins remain parent-owned.

The [lifecycle contract](../../../../specs/242-execution-lifecycle/spec.md#lifecycle-contract-for-subsequent-integration)
requires existing definitions, schemas and identifiers to remain readable, with versioned adoption
and no implicit conversion of active executions. Library payload 1.10.0, Elsa authored structure
1.0.0 and the private-state alias/version are three different contracts.

| Stored surface | Current shape | Required proof before switch acceptance |
|---|---|---|
| Private state | Alias `Elsa.Bpmn.ExecutionState`, version 1; `activeChildren`, token `producingActivityExecutionId` | Explicit version-aware reader/transition; a direct read into the new model silently loses both fields |
| Library state | `activeWork`, `producingWorkHandle`, optional `stoppedHandle`; legacy `pendingFault` remains readable | Preserve original deferred fault and all relevant race/loop/compensation/diagnostic fields; do not equate readable JSON with safe resume |
| Canceled child correlation | Old private blob has no stopped handle; child state retains its execution ID and parent/provenance | Recover exact correlation from durable child state before new pruning; test canceled/late completion and fault separately |
| Replay/retention | Existing accepted work and parent-consumption identities | Preserve capable readers and pending commands; no conversion, cleanup or rollback may strand or resurrect work |

The [isolated state-shape probe](https://github.com/elsa-workflows/elsa-foundation/issues/2735#issuecomment-6394969015)
demonstrated the direct-read loss with five assertions using the actual legacy model writer and
public 0.5.0 model assembly. Its fixtures were synthetic typed states, not captured execution or
migration proof. Source tracing identifies a candidate recovery path: `BpmnScheduler` puts the
token ID in **`ActivitySchedulingProvenance.ExecutionPathId`**; normalized child state preserves it,
and cancellation upserts a terminal child row. Request metadata `bpmn.tokenId` is not copied into
explicit provenance metadata; only operation keys are merged there. Read all parent-scoped child
rows, including terminal children, or load a callback's exact child ID. Validate parent, node/element,
iteration and uniqueness; do not rely on the live-child-only projection or first-match selection.
Historical completeness and re-arm uniqueness still require actual fixtures.

First capture/reload cases: a resolved event race with a canceled loser and a trailing live catch;
parallel multi-instance work with distinct identities; and a verified deferred-fault state. Capture
parent state, child rows, queue/outbox and bookmarks at one coherent checkpoint. A separate controlled
case must establish loser-completion-already-queued before winner cancellation. Same-provider tests
and “both completions in one drain” do not prove that order or a new-provider restart.

## Interchange, bindings and Studio

The [Elsa authored envelope](https://github.com/elsa-workflows/elsa-foundation/blob/98efed4fc2c07337ad0b30128b60f462b8ddcff0/Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Models/BpmnAuthoredStructure.cs)
and [Studio mirror](https://github.com/elsa-workflows/elsa-foundation/blob/98efed4fc2c07337ad0b30128b60f462b8ddcff0/Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnTypes.ts)
are adapters, not the library's definitions/execution-state/work-binding schema roots. Keep the frozen
public authoring fingerprint; an optional library field is not permission to replace Elsa's schema.

| Binding / data | Existing Foundation mapping | Adoption treatment and proof |
|---|---|---|
| `timerWait` | `Delay` duration | Preserve duration and durable resume |
| `messageWait`, `signalWait` | `Event` | Preserve exact name/correlation and listener role |
| `messagePublish` | `PublishEvent` | Preserve delivery behavior and outcomes |
| `callProcess` | `DispatchWorkflow` when Elsa workflow ID is supplied | Preserve called-process identity, wait mode and called-workflow result/error/escalation forwarding |
| `nestedProcess` | Child `BpmnProcess`, `CanStartWorkflow=false` | Preserve scope/body identity and avoid duplicate root triggers |
| `unboundTask` | Host authors/binds it later | Preserve import/editing; Unknown is not executable availability |
| `timerDateWait`, `timerCycleWait`, `signalPublish` | No mapping; current default falls through to null | Handle explicitly, retaining descriptors and reporting capability/readiness before execution; test any claimed implementation, never silently erase work |
| Typed scripts and condition expressions | Not represented as equivalent typed authored fields | Preserve supported data deliberately or report exact degradation; do not flatten expressions into outcome labels |
| Foreign extensions / QName-value namespace provenance | Current exporter starts with empty extensions | Library 1.10 support does not imply Elsa XML/Studio fidelity; retain typed data and mirror it if supported, otherwise declare the limitation |

Keep the existing documented cuts visible while bringing the host to the selected profile. This
is staging of #2735/#2907, not removal of the program's requested BPMN outcome. A relative duration
is not an absolute date or a recurring schedule; a message publisher is not automatically a signal
broadcaster. Library 0.5.0 itself retains bounded timer-cycle limitations, including counted
non-interrupting event-subprocess repetition (separate upstream PR91); do not advertise unsupported
semantics from a binding discriminator alone. Its optional availability assessment preserves import
results, classifies every descriptor after host inventory exists, and requires Available rather than
Unknown for readiness. It does not choose host capabilities or replace publish validation.

Concrete translation gap to fix with the switch: the current importer retains element `DefaultFlowId`
but does not populate the authored flow's `IsDefault` marker. The pinned XML reader also leaves
`BpmnSequenceFlow.IsDefault` false: it represents XML defaults through the source element's
`DefaultFlowId`. Derive the Studio edge marker from that reference, then pin import → Studio → export
preservation. Simply copying `flow.IsDefault` would not fix it. XML export can already emit the
default through `DefaultFlowId`; the demonstrated gap is the Studio edge marker, not necessarily
loss of the XML default.
Also prove host-supplied ordinary-task compensation, and Call Activity escalation with both
interrupting and non-interrupting behavior. The released supported-constructs document still calls
Call Activity escalation “unreleased main” although the 0.5.0 release includes it; that documentation
discrepancy is not an interpreter gap or a reason to narrow host behavior.

## Current executable baseline and unchanged public fingerprint

Two separate local test invocations at the Foundation baseline passed:

| Suite | Executed / passed | Failed / skipped | Differential against the frozen capture |
|---|---|---|---|
| `Elsa.Activities.Bpmn.Tests` | 251 / 251 | 0 / 0 | All 247 old case names remain, plus four additions; 242 unique methods |
| `Elsa.Activities.Bpmn.Interchange.Tests` | 107 / 107 | 0 / 0 | One added nested-nonstarting-process case; one previously removed registration-only case |

The four runtime additions cover the decision's echoed-output secret-restriction declaration,
fixed-at-publish start configuration and its secret refusal, and exact withheld collection-variable diagnostics. They
already have rows in the 242-method audit. The removed
`ActivitiesBpmnInterchangeFeatureTests.ConfigureServices_RegistersImporterAndExporter` is a
**pre-existing** deletion in [PR2608](https://github.com/elsa-workflows/elsa-foundation/pull/2608),
commit `a82956af9f10eab0ab007fa4bcfd8d7aa17b821f`: its registration objective moved to
[`FeatureCompositionTests`](https://github.com/elsa-workflows/elsa-foundation/blob/98efed4fc2c07337ad0b30128b60f462b8ddcff0/Essentials/tests/Architecture/Tests/FeatureCompositionTests.cs).
The nested-process import test is not its replacement proof merely because the total is unchanged.
No tests are deleted or renamed by this parity report.

The immutable [authoring fingerprint](../bpmn-extraction-baseline/activity-contract-fingerprint.json)
has SHA-256 `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
These source-bound baseline tests and unchanged artifact do not prove a future adapter preserves it;
the switch must compare actual published descriptors and retain the same slot, kind/schema, inputs
and outcomes. The `Cancelled` outcome remains structure-dependent.

## Switch and acceptance order

1. Review this parity result and resolve any concrete neutral gap upstream before pinning. None was
   identified here; the observed differences above belong to deliberate library fixes or the host.
2. Specify and review the versioned adapter/state/binding contract. Capture legacy runtime fixtures
   before deleting their writer, then implement the host against published packages. Remove duplicate
   interpreter/graph/behavior code, including host `PruneForPersistence`, and add the issue's
   deletion/name-collision guard.
3. Run affected runtime/interchange suites, real persisted recovery and exact parent-outcome cases,
   native Sequence/Flowchart compatibility, publication/import/export and relevant Studio browser
   journeys; prove behavioral regressions with a revert/mutation control. Run architecture/maps and
   exact-head hosted gates. Do not infer these from the baseline above.
4. Verify resulting main, then accept ADR 0063 in the final ordered PR with delivered behavior and
   limitations. Keep [#2898](https://github.com/elsa-workflows/elsa-foundation/issues/2898) and all six
   [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907) production criteria open until
   their own direct/embedded hosting, task/incident/timer/recovery/API/Studio evidence is satisfied.

This report completes the comparison, not the engine switch, host qualification or full program.
