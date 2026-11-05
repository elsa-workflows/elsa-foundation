# PR3410 author review round 2/6 — independent correctness lens

Reviewed head: `bbb21b25b1a2fa40c900d4374c2a979ff21377f4`.
Merge base: `931123c7ed45aa896539d857c6227a267d08b28f`.
Result: **two actionable P2 correctness findings**. This is one independent lens of the complete PR round; it is not another round or a component/spec review.

## Scope inspected

Read the supplied current org protocol, repository AGENTS and contributor review checklist, spec1017 spec/plan/tasks/data model/hosting contract/quickstart, parent program direction and current issue/PR metadata. Enumerated all 74 issue2898 comments and 180 PR3410 comments, including substantive checkpoint/decision content, the round1 receipt6516922901, scanner comments and repeated CLA boilerplate. Reviews/review comments/thread snapshots are empty. Historical receipts are treated as source-qualified evidence, not proof that this head is clean.

The full base-to-head file/stat and production/test diff scope were inspected rather than only the last repairs. Detailed correctness tracing covered model definitions/materials, hashing and native wire additions; API caller/auth/replay order; direct driver validation/evaluation/retry/cancellation/timer state; native worker/embedding source, admission, result and settlement boundaries; protocol registration/routing/ownership; scheduler preclaim/refusal/acknowledgement; checkpoint participant CAS/transaction/marker paths; EF state/material readers, schema writer/upcasters, all provider migration families; publication/dependency closure/root leases/GC; and the latest retained Design migration/history binding repair. Tests and qualification controllers/receipts were checked for relevant coverage and limits. Generated migration target models, lockfiles and large retained stores were assessed in their schema/compatibility context; the 750MB binary full-pr.diff was not loaded wholesale, and this lens does not rehash every retained evidence blob.

## F1 — P2: A valid model that completes on its first evaluation cannot run inside RunBpmn

Location: `Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs:37` (Running-only initial-parent admission shape). The producer is `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs:339`, with initial completion and native result staging at lines285–297 and331–335.

A format1 process with plain StartEvent → EndEvent and an empty human binding inventory is accepted by publication/Graph and directly completes at revision1/TerminalSettled. The contract lists plain start/end as supported for direct and RunBpmn. Place that same published model in the real native RunBpmn activity. Its first interpreter evaluation is already complete, so the driver proposes TerminalPendingSettlement plus the native return intent. It also correctly attaches ExpectedModelParentWait to order acceptance against cancellation. EnsureModelParentAdmissionShape requires the initial state to be Running and throws before the state, receipt and return outbox can commit. The native RunBpmn remains BookmarkWaiting indefinitely and model recovery cannot make progress.

Private real feature/mailbox/SQLite probe result (`probe-current-instant-output.txt`): direct start Accepted/TerminalSettled revision1; native bookmark count1; resumption dispatch failure `Model child admission requires its initial decision and captured live native parent wait.`; embedded model remains null; native workflow Running and activity Suspended/BookmarkWaiting. Repeated sweeps retain the failure. No source was retired in this run.

Meaningful comparison (`probe-current-human-control.txt`): changing only the process to Start → UserTask → End, completing the actual human work through IBpmnModelRuntime, and sweeping the same real RunBpmn scaffolding completes the native workflow/activity. This confirms that the fixture, registration, typed native wait and general delivery path work.

Fix direction: permit a valid first terminal decision to carry the same exact native parent-wait condition and atomically retain its initial result delivery; do not omit the cancellation/admission compare as a workaround. Add direct/embedded no-work model coverage and ensure native result settlement converges after recovery.

## F2 — P2: Retiring a publication prevents replay of its already accepted direct-start command

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs:85–87`.

StartAsync looks up and requires the publication to remain live before deriving the deterministic execution ID and reaching the retained input receipt in DispatchAsync. If a start response is lost and the publication is normally retired before the caller retries, an identical start by the same actor/tenant now throws BpmnModelResourceMissingException/HTTP404 although the exact accepted execution and original start receipt remain retained. Publication retirement should stop fresh admission, while a retained matching command still has the contract's reproducible outcome. The native WorkflowStartDispatcher deliberately resolves keyed duplicates before publication lookup for this same case.

Private normal-port proof (`probe-current-retirement-output.txt`): publish; start Accepted/TerminalSettled revision1; same start replays successfully; call the existing IWorkflowExecutableSourceReferenceStore.RetireAsync (returns true); repeat the identical start and observe BpmnModelResourceMissingException. FindCommandAsync immediately returns the original Accepted revision1 outcome with Replay=true. No execution, input, queue, receipt or claim was edited. Source retirement uses the production port also used by this PR's retention/rollback witness.

This violates hosting.md's `Equivalent command replay returns its stored outcome` and FR007's identical-retry result requirement. No source-retirement revocation exception was found in the declared command contract. The failure is loss of the replay response, not duplicate semantic progression.

Fix direction: after scope/caller validation, resolve the caller's deterministic existing start/queued input and compare its exact retained source/payload/pin identity before requiring live-reference authorization for a genuinely new start. Preserve conflicting retry and foreign-scope refusal and authorization-before-disclosure. Cover matching replay after retirement and refusal of a new request ID.

## Probe provenance and execution limits

All new probe source/build/output files are under `/tmp/elsa-2898-review-r2-correctness`. No repository file, GitHub state, user host, browser tab or user store was changed. Every private SQLite database was created normally by the harness and deleted on disposal. No production guard was removed or patched. Final `git status --short` is empty.

Reproduction:

```sh
dotnet build /tmp/elsa-2898-review-r2-correctness/probe/Probe.csproj --nologo -v quiet
dotnet /tmp/elsa-2898-review-r2-correctness/probe/bin/Debug/net10.0/Probe.dll
dotnet /tmp/elsa-2898-review-r2-correctness/probe/bin/Debug/net10.0/Probe.dll --human
dotnet /tmp/elsa-2898-review-r2-correctness/probe/bin/Debug/net10.0/Probe.dll --retire
```

The harness was compiled through ordinary dotnet/build-slot PATH. Builds pass with zero warnings/errors. Probes exit0 because failures under review are captured as observations. The first exploratory build lacked references/NodeId and first exploratory run lacked the private SQLite native library; these setup diagnostics are not product findings. The proper existing osx-arm64 SQLite native library was then copied into the private probe output directory.

The first exploratory run used old Debug production assemblies at51605da83897; `probe-output.txt` remains explicitly historical. It was repeated with the current Release Workbench product set at executing source `4f46ac55c482b3b68932ca74f147439bfdacda49`. The current outputs record product source and SHA256, including:

- Elsa.Activities.Bpmn: `8aa1ec28c0792238a265d913cdceb33bf52f3c4d344fde4135930218302bde47`.
- Elsa.Workflows.Runtime.Core: `876af7240ee20e816c34cb450a6feb8208dd415dc250509d7ee991d5e2345d68`.
- Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore: `45e7ba9647767b53dea6df25cda7c370982ff30ef0019c8f401c49a5492c69ba`.

The testing harness/Nodes helper assembly retains source51605da83897; both helper source files are unchanged at the reviewed head. `probe-manifest.json` retains Git blob equality for the affected guard, direct driver, model runtime, embedding boundary/delivery, EF checkpoint writer and those two test helpers. The production source files are byte-identical between executing4f46 and reviewedbbb21. These are current-source-equivalent in-process product-path diagnostics, **not** fresh-process HTTP-host qualification, a current-head product rebuild, a replacement for provider/whole-suite CI, or a post-main result. No exact-head CI success is inferred.

## Candidates not retained and existing holds

A model/native ID-collision candidate was dropped: the normal actor route recognizes committed model identity and refuses the incompatible native command. Direct privileged low-level writes alone are not sufficient normal-user evidence. Destructive migration downgrade is explicitly outside this feature's rollback method, so the virtual included-baseline history did not yield an in-scope downgrade finding. No additional concrete failure survived the remaining source checks.

Existing unanswered API/version-magnitude decision, needs:decision, deferred GitGuardian39189012/neutral scan, required exact-head CI/merge/post-main work are known delivery holds, not new correctness defects. #2898/#2907/#2908/program remain open. #3242 and Orleans/task-runner deferrals remain unchanged; delivered #2735 was not restarted.
