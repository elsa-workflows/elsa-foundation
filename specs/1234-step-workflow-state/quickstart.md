# Validation guide: Step workflow state

This is the implementation acceptance guide, not a record of tests already run. Use the approved spec, exact implementation head and existing test harnesses. Keep raw receipts on the implementation PR. CI is deferred to the initiative; local failures/not-run states remain visible.

## Preparation

Use the stream's isolated checkout, current initiative baseline and the repository's build-slot wrapper. Determine the actual SDK/project target from `the current Directory.Build.props and SDK configuration` and project files. Use an established permitted Linux execution path if Windows Application Control blocks a build/test; record that platform limit and the Linux source SHA. Do not bypass a build queue or disable policy. Check load/core count before calling a timeout a product failure.

## Affected-project suites

Run these existing projects sequentially, using filters while developing and the affected whole suites for final evidence. Add exact runtime test names in the receipt once implemented; do not report the below planned suites as executed.

```powershell
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter 'FullyQualifiedName~StepWorkflowState|FullyQualifiedName~StepVariable|FullyQualifiedName~ScriptBodyWrites'
dotnet test Essentials/tests/Activities/Scripting/Tests/Elsa.Activities.Scripting.Tests.csproj
dotnet test Essentials/tests/Expressions/JavaScript/Jint/Tests/Elsa.Expressions.JavaScript.Jint.Tests.csproj
dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter 'FullyQualifiedName~StepWorkflowState|FullyQualifiedName~StepVariableWrite'
dotnet test Essentials/tests/Workflows/Publishing/Tests/Elsa.Workflows.Publishing.Tests.csproj
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter 'FullyQualifiedName~EventCorrelation|FullyQualifiedName~WorkflowIntrinsic|FullyQualifiedName~Checkpoint'
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj --filter 'FullyQualifiedName~RunJavaScriptSetsWorkflowState'
```

Use existing EF-backed test infrastructure in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests` and affected Activities.Runtime integration tests for atomicity/recovery; an in-memory mock alone does not prove the provider transaction. Keep test-tier membership unchanged unless a new project is unavoidable and explicitly justified.

## Required journeys and controls

| Journey | Expected result | Negative control |
|---|---|---|
| Native + imported script set output/correlation/name and variable, then read/return state | All new state and result/outcome commit together; same-body reads use pending values | Throw/cancel after all writes; no successful effect or successor |
| Repeat output write and null identity clear | Last assignment wins; missing output differs from explicit null; header/projection agree | Undefined output, blank name, number identity and caught refusal reject entire evaluation |
| Identity-only or output-only body with capture conflict | No partial recorded-completion acceptance | Baseline predicate that checks only variable records must fail this proof |
| Stale variable base mixed with identity/output | Existing warning/LastWins default; Fault opt-in rolls back all state | Fault-mode variable conflict rejects identity/output, including staged captures |
| Old wait, identity change, new wait explicitly bound to current identity, restart | Old/new deliveries resume only the corresponding captured keys | Rebinding old bookmark or automatically inheriting identity fails assertions |
| Suspend, resume, later fault | Suspension writes survive; failed resume writes do not; fresh session | Reused pending session changes or whole-invocation rollback fails |
| Provider failure before transaction commit; redelivery after durable marker | All-or-none header, projection, outputs, variables and continuation; one accepted delivery | Fail injected participant/save; no partly visible checkpoint |
| Restricted placement and expression evaluation | Publish and runtime refuse input/structural/background/closed graph writes | Bypass authoring/pinned placement checks; identity-only writer still denied |
| Conversion lexical/value semantics | Local helper names untouched; direct calls preserve order/undefined | Alias/root collision/spread/wrong arity refused with stable code/location/guidance |
| Protected input/read, including read after assignment | Output policy retained; public identity/name write refused; value-free error | Search actual API/inspection/screenshot evidence for sentinel value; zero exposure |

Keep S-7 graph/reusable refusals and `setInput`/`setResult` refusal tests. Scope receipts explicitly state that graph intrinsic delivery and reusable caller-output acceptance are not delivered here.

## Studio acceptance

Follow `docs/contributing/studio-ux-acceptance.md` and `tools/ux-acceptance/run.py` (inspect its current usage before running). Goal: identify a node's state effects, run/import it, and inspect committed and failed results without reading code. Reference: current Activity Properties effect section and workflow instance details/timeline.

Cover native/imported, known/computed effects, empty/populated outputs, cleared identity, success/fault/cancel/refusal, protected values, keyboard/focus, narrow view and both themes. Run the affected whole frontend suites plus selected composed-host journeys. Retain downloadable current-head screenshots, trace and raw reports. An independent reviewer must open screenshots and inspect the composed experience; assertions alone are not visual acceptance. Failed/not-run states stay visible until fixed/rerun. CI-deferred does not waive these local journeys.

## Final receipt

Record reviewed PR head, tested source head, exact commands/counts, negative-control failures, provider/source environment, retained artifact links, independent review round and UX verdict. State CI **deferred/not waited** and list the formal initiative approval/merge gate separately. Do not reuse historical S-7 receipts as current S-13 execution evidence.
