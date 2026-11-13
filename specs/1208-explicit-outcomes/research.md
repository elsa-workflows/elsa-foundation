# Research: Explicit outcomes

**Spec**: [spec.md](spec.md) · **Date**: 2026-11-04 · Line numbers are from `origin/main` at `73040cd7b`.
Paths are under `Essentials/src` unless they start with another root (`Essentials/tests`, `Extensions/Elsa3`,
`Studio`). "Inferred" means traced in code, not run.

## R-0 What the code survey changed

The #3389 draft leaves five things to S-3: the API, the record shape, the multi-outcome declaration, the fallback, and
the changes to scheduling, joins and spec 266. The survey settles the last one (the engine is already close, R-3) and
adds five questions where the draft is silent or does not match the code:

The draft compared is #3389 at `c9dcb326b`; all five are applied to it in `18cccb00b`, F-1 as the `Done` rule.

| Finding | Draft says | Code shows | Question |
|---|---|---|---|
| F-1 | Unspecified means the default `Done` | A RunJavaScript with possible outcomes has no `Done` in its pinned set (R-4) | Q2 |
| F-2 | Unspecified and empty stay distinct through parent handling | Non-routing parents and the root have no rule for empty; the graph boundary requires exactly one outcome (R-3) | Q3 |
| F-3 | The connections of every selected outcome are scheduled | Two taken connections to the same node fault the engine (R-3) | Q4 |
| F-4 | Studio exposes affected connections for repair | Publication never checks connection ports (R-8) | Q9 |
| F-5 | The activity contract declares multi-outcome completion | A Switch allows several only in one mode, which is per node (R-6) | Q10 |

## R-1 RunJavaScript routes by its return value

- `Activities/Scripting/Activities/RunJavaScript.cs`:
  - L34 `[ActivityValueOutcomes(PossibleOutcomesInputKey, UnmatchedOutcome = UnmatchedOutcome)]`; L38
    `UnmatchedOutcome = "Unmatched"`; L62–63 `PossibleOutcomes`.
  - L76–79: with possible outcomes, `Complete(completion, SelectOutcome(result, outcomes))`; without, `Complete(completion)`
    (`Done`).
  - L89–101 `SelectOutcome`: a JSON string or number that is in the list, else `Unmatched`.
  - L22–31 (doc comment): routing by return value was chosen over Elsa 3's `setOutcome()` because "an Elsa 4 completion
    carries exactly one outcome".
- The evaluator returns `ValueTask<JsonElement?>` and has no outcome channel
  (`Expressions/JavaScript/Core/Contracts/IJavaScriptScriptEvaluator.cs`; `Expressions/JavaScript/Jint/Services/JintJavaScriptScriptEvaluator.cs`
  L12–24). The same evaluator backs the REST endpoint `Workflows/Runtime/JavaScript/JavaScriptExecutionApi.cs` L77–80.
- No `setOutcome` / `setOutcomes` host function exists in any Elsa 4 engine (JavaScript, Liquid). There is no C#
  scripting engine.

## R-2 The completion transition carries one outcome

Single string today:
- `Activities/Runtime/Core/Models/ActivityTransition.cs` L15, L93 (`string outcome = "Done"`), L33, L112 (non-blank
  guard), L190 (`IActivityCompletionTransition.Outcome`), L6 ("Exactly one closed decision").
- `Workflows/Runtime/Core/Models/RuntimeStructuralContinuation.cs` L30, L41–47.
- `ActivityCompletion.OutcomeKey` (`Workflows/Runtime/Core/Models/ActivityExecutionValueFlowRecords.cs` L207–242, non-blank
  at L220).
- `ActivityCompletionProjector.cs` L56–57 (the `VF-ACT-006` check, one name), L129, L166. On `main` after #3801
  (`4a81500f8`) the outcome check is at L55; the behaviour is unchanged.
- Background: `BackgroundActivityCompletionCodec.cs` L54–57 (`BG-ACT-005`), L67; `Workflows/Runtime/Core/Models/BackgroundActivityWork.cs`
  L149, L177.
- Handlers that build a one-name list: `WorkflowInvokeActivitySchedulerWorkHandler.cs` L442–447, L462–466;
  `WorkflowResumeBookmarkSchedulerWorkHandler.cs` L553–555; `WorkflowParentActivityCompletionSchedulerWorkHandler.cs`
  L747; `StructuralCallbackEvaluation.cs` L434; `ChildControlDelivery.cs` L74; `WorkflowIntrinsicExecutor.cs` L181–217,
  L325–349; `UserTaskRuntime.cs` L103; BPMN `BpmnNativeWorkBoundary.cs` L304, L319 and `BpmnDirectExecutionDriver.cs`
  L315, L337.

Already a list: `RuntimeCompleteActivityCommandPayload.OutcomeNames` (L22, L47, L91–115; allows empty);
`ActivityChildCompletedContext` (L15–46, L60); `ExecutionPath.LastOutcomeNames`; `FlowchartPolicyContext.OutcomeNames`;
`SchedulerWorkHandlerHelpers.NormalizeOutcomeNames` (L128–141, `defaultToDone`) and `ReadCompletionOutcomeNames`
(L149–160).

## R-3 Scheduling and joins already handle several names

- `Activities/Flowchart/Internal/FlowchartExecutionEngine.cs` L143: `SelectOutboundConnections(node, outcomeNames)`; L144–145
  **faults on duplicate targets**; L147–162: no match completes the path and routes untaken connections dead; L164–199:
  one arrival per taken connection, untaken ones dead, then joins are released.
- `FlowchartGraph.cs` L173–188 matches connections whose port is in the set; L457–464 `NormalizeOutcomes` turns an
  **empty set into `Done`**; L151–152 the same duplicate-target fault in `SelectTargets`. `FlowchartPolicyConnectionSelector.cs`
  L31–32 has the same empty → `Done` rule.
- Joins: `FlowchartOptions.JoinPolicyKind` defaults to `DeadPathJoin` (L21); `FlowchartJoinCoordinator.cs` L131–240
  routes untaken connections dead and carries deadness through (ADR 0064).
- Node policies (`FlowchartPolicyKinds.cs`): `parallelFork` schedules all outbound connections; `decision` and
  `inclusiveFork` schedule the matching ones.
- Inferred: a selection of several names already produces live arrivals on the matching ports and dead arrivals on
  the others. The two gaps are the duplicate-target fault (F-3) and empty → `Done` (FR-003).
- Parents: `GraphActivity.cs` L124–131 requires **exactly one** mapped outcome; `WorkflowCompleteActivitySchedulerWorkHandler.cs`
  L228–229 returns `NotTerminal([])` for an empty list; `StructuralParentEvaluationSupport.cs` L421 calls
  `NormalizeOutcomeNames(defaultToDone: true)`, which turns empty into `Done` (F-2).

## R-4 Outcome sets are pinned at publication, without `Done` for RunJavaScript

- `Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs` L623–650 `ResolveOutcomes`:
  `[ActivityOutcome]` keys, Switch case labels, BPMN `Cancelled`, then value-derived outcomes; `Done` only if the set
  is still empty. L659–688 `AddValueDerivedOutcomes`: a non-literal collection binding is refused (L670–671); items are
  strings or a number's raw text, **not trimmed** (L676–683); the unmatched catch-all is added when any item is (L686–687).
- Inferred: RunJavaScript has no `[ActivityOutcome]`, so with possible outcomes its pinned set is the items plus
  `Unmatched`, without `Done` (F-1).
- Other users of `[ActivityValueOutcomes]`: `SendHttpRequest.cs` L56–59 (static `Done`, `Failed`, `Timeout`, and the
  unmatched status code), `CreateUserTask.cs` L10 (default `Unmatched`).
- **SendHttpRequest's dead `Done` port.** With expected codes, `DetermineResponseOutcome` (`SendHttpRequest.cs` L190–200)
  returns a code or the unmatched status code, never `Done`, but `Done` stays in the pinned set from the static
  attribute (L56). Studio shows a `Done` port that never fires (Q8).
- **Where `Done` is declared (Q2).** Activities that do not branch declare only `Done` (`WriteLine.cs` L14, `Delay.cs`
  L21, `Sequence.cs` L17, `Flowchart.cs` L30, `PublishEvent.cs` L36, BPMN). Activities that always branch declare no
  `Done`: `If.cs` L28–30 (`True`, `False`, `Break`), `Switch.cs` L26–27 (`Default`, `Break`, plus cases). No ADR records
  this as a decision; it follows from one outcome per completion. Model A writes it down. Guard test:
  `Essentials/tests/Activities/Design/Tests/Contracts/ActivityValueOutcomeSourceTests.cs`.
- Design facet: `Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs` L352–406 writes `elsa.outcomes`
  with `dynamicOutcomes: { kind: "valuePerItem", source, unmatched }`.
- Studio: `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter.ts` L1131–1153 (`getActivitySourcePorts`),
  L1189–1217 (`readValueOutcomePorts`, appends the unmatched port). Ports are recomputed when a node is projected (L982).

## R-5 `VF-ACT-006` is a message prefix, and there is no fallback

- `ActivityCompletionProjector.cs` L57 throws `InvalidOperationException("VF-ACT-006: Outcome ... is not declared")`;
  the invoke handler catches it as `ActivityFaulted` (`WorkflowInvokeActivitySchedulerWorkHandler.cs` L548–561). Tests
  match on the message (`Essentials/tests/Activities/Runtime/Tests/ActivityCompletionContractTests.cs` L46).
- No fallback outcome concept exists. The closest are the activity-declared catch-alls (`Unmatched`, the unmatched
  status code), Switch `Default`, and `BpmnDecision`'s blank → `Done`.

## R-6 Which activities could select several

- Every Elsa 4 activity selects exactly one outcome today: Switch (`Switch.cs` L70–113, first match or `Default`), If,
  Parallel, Graph `Complete` (`Complete.cs` L35–56), the Finish intrinsic, SendHttpRequest, BpmnDecision.
- A Switch would allow several only in "match any" mode, which is node configuration (F-5). The same holds for
  RunJavaScript only if its script returns `complete` with several names, but that cannot be known for a computed call, so RunJavaScript
  allows several always.
- Spec 266 (#3286, D-2a) already reads "the node's activity contract allows several outcomes" to treat a node as an
  inclusive fork. The current unordered-write analysis on `main` (`ValueFlowValidator.cs` L157–179, L530–565) ignores
  ports, so it already treats every fork conservatively.

## R-7 The Elsa 3 converter

- `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs` L295–303: RunJavaScript carries
  `PossibleOutcomes` and has the rule `[Outcome("Done", "Unmatched")]` (spec 239 R8).
- `Elsa3FlowchartGraphBuilder.cs` L244–258 applies the rule **unconditionally**, whether or not possible outcomes are
  set; `EmittedOutcomes` (L278–295) adds the items and `Unmatched`.
- Inferred (#3295): with no possible outcomes, the converted node pins only `Done` (R-4) and the connection is on
  `Unmatched`, so it is never followed. Fixture `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/reusable-reads-result.json`
  has this shape; it is converted but not run (see the dependency below).
- `setOutcome` / `setOutcomes` are refused with `VF-IMP-005` (`Elsa3ExpressionRewriter.cs` L510–522; spec 247 FR-019).
  Tests: `Elsa3JavaScriptHostFunctionTests.cs` L183–184, `Elsa3JavaScriptConversionTests.cs` L118. The corpus has none
  (spec 247 `spec.md` L46).
- Switch "match any" is refused with `ELS3-ACTIVITY-UNSUPPORTED-MODE` (`Elsa3SwitchValueSynthesisRewrite.cs` L26–38).
- Complete with several outcomes is refused (`Elsa3TerminalPlacementRewrite.cs` L101–107, "Elsa 4 completes with exactly
  one").
- **Elsa 3's always-firing `Done` is already mapped onto every branch** (model A, Q2): If `Outcome("Done", "True",
  "False")` (L39), Switch `Outcome("Done", "{match}", "Default")` (L67), DispatchWorkflow `Outcome("Done", "Dispatched",
  "Completed")` (L260). Exactly one branch fires per run, so the converted connection is always followed.
- SendHttpRequest is the exception: `Outcome("Done", "Done")` (L218), with only the warning
  `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE` (L216–224; text `Elsa3ActivityNormalizer.cs` L1041). With expected codes the
  Elsa 4 node never completes with `Done`, so the path is lost (Q8). FR-029a maps it like If.
- **Not verified:** whether Elsa 3's SendHttpRequest also fires `Done` on "Failed to connect" and "Timeout". The
  elsa-core source is not in this repository; T001 checks it before FR-029a's mapping is fixed.
- Diagnostic `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING` (`Elsa3ActivityDiagnosticCodes.cs` L128–129; text
  `Elsa3ActivityNormalizer.cs` L1049) describes routing by return value and goes with it.

- **Converted RunJavaScript cannot run yet (dependency).** `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ImportedReusableRunsTests.cs`
  (L190–194 on `main`) records that any converted RunJavaScript faults on its default arguments, which is why
  `reusable-reads-result.json` is converted but not run. Two open issues are behind it: #3195 (a converted
  RunJavaScript without arguments faults at run time; board S-0) and #3287 (one that reads a variable or input is
  refused at publication; board S-4 and S-8). S-3's convert-publish-run evidence (US2, FR-030, and #3389's required
  execution evidence) needs #3195 fixed, and #3287 for scripts that read variables. They are dependencies of S-3's
  delivery, not of its requirements.

## R-8 Publication does not check connection ports (general defect)

- `Activities/Flowchart/Internal/FlowchartStructureHandler.cs` L71–85 `CompileExecutableStructure` copies connections
  without checking their ports. No design validator and no publishing service checks a connection's source port
  against the source node's outcomes (searched `Workflows/Design/Validations`, `Workflows/Publishing`,
  `Activities/Flowchart`). The only port check is the Elsa 3 converter's `GraphOutcomeUnmapped`.
- Inferred: a mistyped port publishes and is never followed; its path is routed dead. #3295 is one instance.
- Related: value-derived names are pinned untrimmed (R-4), while connection ports are trimmed on read
  (`FlowchartEndpoint.cs` L14, L23–24), so an authored name with surrounding spaces can never match.
- This does not depend on the initiative, so it has its own issue, #3809. S-3's FR-019 fixes it if it is
  still open.

## R-9 Script analysis for literal names

Acornima 1.6.1 (`Directory.Packages.props` L15) is already used for script parsing in `JavaScriptBindingSyntax.cs`,
`JavaScriptExpressionToolingProvider.cs` L67–74 and the Elsa 3 rewriter. No publication diagnostic reads
`RunJavaScript.Script` today. FR-015 adds one: find calls of the script outcome API whose arguments are string
literals (or arrays of them), and compare them with the node's set.

## R-10 Documents to update

ADR 0045 L103 and §6 (by #3389); spec 095 `contracts/activity-invocation-contract.md` L70–76, L150–157, L258 (by
#3389); spec 239 `research.md` L53–54, L95, R8; spec 236 `member-map.md` L188–189, `research.md` L296, L303; spec 247
`contracts/javascript-conversion.md` L43 and FR-019; spec 267 `spec.md` L59; `docs/reports/elsa-4-activity-behavioural-drive-2026-08.md`
L131; `docs/reference/elsa3-migration-readiness.md` L24, L80; `docs/user-tasks.md` L13; `Extensions/Elsa3/src/Mapping/README.md`
L97, L213; the XML docs of `RunJavaScript`, `ActivityValueOutcomesAttribute` and `ActivityTransition`.

## R-11 Tests that pin today's behaviour

`Essentials/tests/Activities/Scripting/Tests/RunJavaScriptExecutionTests.cs` L86–141; `Essentials/tests/Activities/Behavioral/Tests/Drives/ScriptingDrives.cs`
L11–26; `activity-contract-surface.baseline.json` L1670–1677; `Elsa3ActivityResolverTests.cs` L148, L157;
`Elsa3ConversionDiagnosticsTests.cs` L250–251; `ActivityCompletionContractTests.cs` L46; Studio
`__tests__/dynamicOutcomePorts.test.ts` L7–29. `Essentials/tests/Activities/Testing/WorkflowExecutionHarness.cs`
L770–790 mirrors `ResolveOutcomes` without the value-derived outcomes; S-3 brings it in line.

## Decisions taken in this spec (not open)

- **Engine:** keep the Flowchart engine; change only normalization (empty stays empty), the duplicate-target rule and
  the declarations. Rejected: a new multi-outcome scheduler (R-3 shows it is unnecessary).
- **Literal check:** Acornima over the script at publication (R-9). Rejected: a runtime-only check (D-4 requires
  publication to catch typos).
- **`VF-ACT-006`:** a fault code on the fault record, not a message prefix (R-5).
