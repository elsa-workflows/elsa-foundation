# PR3410 author review round 3/6 — independent correctness lens

Reviewed exact head: `fcf551c789c37c722b3b07d0c1cf4aecb56a16ad`.
Adopted main / merge base: `184759ce53bc251ccc81b07ec3980d804ad8fd76`.
Result: **two actionable P2 correctness findings**. This is the correctness lens of the complete third PR round, not a new component/specification round or merge clearance.

## Scope and protocol

Read repository AGENTS, the supplied fresh org protocol, contributor review checklist, relevant ratified constitution gates, spec1017 spec/plan/tasks/data-model/hosting contract/quickstart, qualification index and historical round2 reports/receipt/repair qualification. Inspected the fresh PR/issue2898 snapshots and enumerated all 193 PR comments and 78 issue comments, including substantive architecture/qualification/decision content, historical round receipts, scanner comment and repeated CLA boilerplate. The only other commenters are CodeRabbit (one skipped review), GitGuardian (one already tracked finding) and the CLA bot (114 repetitions). Review, inline-review and thread snapshots contain no actionable review content. Existing version magnitude/needs:decision and scanner holds are recorded below rather than counted again as findings. No current-round peer review output was read.

The review is against the full base-to-head change inventory, retained at `files.txt`, rather than only the six round2 corrections. Detailed source tracing covered the BPMN definition/material/protocol/profile contracts, hashing and legacy native wire; direct-model API/caller/replay order; driver validation, semantic evaluation, human/timer/native-result/retry/cancellation/terminal state; native worker and embedding source/claim/admission/result/settlement boundaries; protocol registry/router/shared ownership; scheduler preclaim/refusal/acknowledgement; checkpoint model/native participant shapes, CAS/transaction/marker order; EF state/material/schema writer/upcaster paths; migrations and retained Design cumulative-history compatibility; publication/dependency closure, root leases and GC; composition/catalog/test-tier/generator changes. Relevant regression/provider/host tests and source-bound qualification controllers/receipts were checked for coverage and limits. Generated migration target models, lockfiles and large retained stores were assessed in their schema/compatibility context rather than dumping giant binary diffs. This lens did not rehash all historical evidence or rerun every whole-project/provider/browser gate.

## F1 — P2: Format2 admits concurrent service work that can strand an accepted native start after fault

Primary location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs:399–401`.
Consequences: the final-fault path at lines290–293, and `BpmnNativeWorkBoundary.cs:51–59` / `BpmnNativeWorkDelivery.cs:64–78`.

The serial-service topology guard applies to definition format3 and timed service format4, but not format2 (the supported original/default FinalFault profile). A format2 process containing one plain StartEvent, one ServiceTask and one EndEvent, with **two outgoing start flows into that same service element**, passes normal `IBpmnModelRuntime.PublishAsync` validation. The released interpreter creates two distinct semantic work tokens and the runtime atomically owes two exact native starts. Two plain starts also pass the same graph validation and create two invocations.

Deliver the first original native start and let that real native worker fault; deliver its actual terminal result and drain that protected model input before the second original start is admitted. The driver records Faulted / TerminalSettled revision2 while preserving the other active Work, its original start source and no cancellation/retirement obligation. Every later delivery of that accepted second source reaches RequireLiveAsync, which permanently refuses a TerminalSettled model. The native-start contribution uses UntilAcknowledged retry, so the source retries indefinitely. The supported cancel command now returns execution-terminal and cannot settle that obligation. A cold process retains the same settled header, one active work, one native worker and retrying original source.

This is a model-owned obligation/lifecycle defect, not a proposal to change native Sequence/Flowchart/CompensationScope fault policy or settle #3242. A transiently delayed second start, independent delivery scheduling, or a process cut between the first worker's result and the second start can expose it. A settled model cannot leave an already accepted start permanently ineligible with no durable disposition (FR005/FR019 and the terminal-settlement contract).

### Independent normal-port reproduction

`native-probe/Program.cs` creates a private feature/mailbox/SQLite runtime. It publishes a real closed native FaultingActivity worker (with its required Message literal), then publishes and starts the format2 model through IBpmnModelRuntime. It uses the existing filtered IRuntimePostCommitOutboxProcessor to deliver exactly one original start, that worker's actual result and then the actor's ordinary RunSchedulerWork drain. No model state, queue, receipt, claim, source or clock is seeded or edited. The filtering controls an admissible delivery order; it does not manufacture source authority.

`native-probe/concurrent.log` and `observations.json` record:

- Initial: Running revision1, two exact Work entries, zero native workers, two pending native starts.
- First original worker: actual native Faulted result, one native worker.
- First result consumption: model Faulted / TerminalSettled revision2, **one Work still active, no Cancellations**, second original start still pending.
- Original second-start processing: FailedRetryable; a normal later retry fails again.
- Public cancellation: Rejected / execution-terminal / revision2.
- `native-probe/cold-retry.log`: a fresh process opens that same normal-produced database, attempts that exact original source again, fails retryably and retains TerminalSettled revision2 / Work1 / native count1.

Controls use the identical runtime/worker: `--single` removes only the second start flow and ends TerminalSettled revision2 / Work0 with no second start; `--policy` uses the existing format3 FinalFault policy and refuses that exact concurrent graph before model admission with the intended serial-topology BpmnModelValidationException. Logs are `single-control.log` and `policy-control.log`. The separate stateless `probe/graph.log` also shows both concurrent graph shapes accepted by the checked driver and the released interpreter's two active works.

Fix direction: make the admitted format2 topology consistent with the supported single-invocation cut, or explicitly retain and settle remaining committed start/work obligations on model final fault. Do not merely drop the source or mark it delivered without positive retirement, remove current-owner checks, or alter existing native composite fault semantics. Add the failure-order regression and keep the original simple format2 material/hash and already accepted recovery boundary intact.

## F2 — P2: Referencing a valid legacy native artifact with an omitted input contract throws NullReferenceException during model publication

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs:302`.

`WorkflowExecutable.InputContract` is explicitly nullable and the retained legacy public constructors omit it. Such native material remains valid and runnable. ValidatePublishedWorkerAsync dereferences `worker.InputContract.Inputs` after finding and matching that material. Selecting its exact published pin in a new native-service binding therefore throws a raw NullReferenceException instead of a contextual supported-profile refusal. BpmnHostingFaultRenderer has no mapping for that exception, so the public publication route's normal 422 domain-validation path is bypassed. The new profile may require an explicit closed input contract; it still needs to refuse this supported legacy shape deliberately rather than crash.

`native-probe/legacy-contract-final.log` independently publishes such a worker through the ordinary production store/source ports, runs it through StartPublishedAsync (the expected real native Faulted status is retained), then calls IBpmnModelRuntime.PublishAsync with its exact pin. Publication fails in ValidatePublishedWorkerAsync with NullReferenceException at lines301–302. The same worker with an explicit empty contract is the successful publication control used by the other cases. No legacy data was fabricated with SQL and no existing store was modified. This probe establishes the runtime boundary exception; the HTTP500 consequence is inferred from the unchanged fault-renderer switch, not claimed as a fresh HTTP host observation.

Fix direction: explicitly guard the absent legacy InputContract and return BpmnModelValidationException with the worker/binding context (or deliberately normalize a known empty legacy contract if the profile authorizes that). Preserve the prepublication refusal and native legacy execution behavior. Cover the nullable public-constructor/material case.

## Probe provenance, validation and limits

All new source, builds, logs and private normal-produced SQLite files are under `/tmp/elsa-2898-review-r3-correctness`; `probe-manifest.json` records file hashes and exact Git blob equivalence. Production binaries come from the frozen Release Workbench set at executing source `e3ec219fcc3a93c949d2669fc585bd1e79152abe`. All BPMN/Runtime/Publishing/shared-EF production source relevant to these probes is byte-identical to reviewedfcf551. Key loaded products:

- Elsa.Activities.Bpmn: `daca65277a4c7d554da6aaa76efa6585ccc7af435275503ac1c03f63fb0a90e7`.
- Runtime Core: `c7b6b2e2384b26dddcd6ebf78d5f400df1a7a86638cad76b644e3ddc741eddf8`.
- Runtime EF: `6c8db6d09cd1c5ed3a523bb51e93939507402177821aebd8e28d994bf1ce1012`.

The testing helper assembly retains source51605da83897; the actual WorkflowExecutionHarness, FaultingActivity and Nodes helper sources used are byte-identical at reviewedfcf551 and their blob IDs are recorded. Probes are compiled through the ordinary dotnet/build-slot PATH; final builds have zero warnings/errors. Machine load was approximately3 on the eight-core machine before compiling. Each final probe process exits0 because observations/refusals are captured. The cold retry is a separate process with ordinary migration/reader composition over the original probe's database. No hosts or browsers are launched and no user's stores/tabs are touched.

Reproduce with the saved private project:

```sh
dotnet build /tmp/elsa-2898-review-r3-correctness/native-probe/Probe.csproj --nologo -v:q
dotnet /tmp/elsa-2898-review-r3-correctness/native-probe/bin/Debug/net10.0/Probe.dll
dotnet /tmp/elsa-2898-review-r3-correctness/native-probe/bin/Debug/net10.0/Probe.dll --single
dotnet /tmp/elsa-2898-review-r3-correctness/native-probe/bin/Debug/net10.0/Probe.dll --policy
dotnet /tmp/elsa-2898-review-r3-correctness/native-probe/bin/Debug/net10.0/Probe.dll --legacy-contract
# Pass the STORE path printed by the concurrent run:
dotnet /tmp/elsa-2898-review-r3-correctness/native-probe/bin/Debug/net10.0/Probe.dll --reopen /tmp/elsa-2898-review-r3-correctness/native-probe/<printed-store>.sqlite
```

The private output uses the existing osx-arm64 libe_sqlite3.dylib copied from the historical review probe. Setup diagnostics remain explicitly separate: an initial compile used a nonexistent activation enum value; a constructor compile lacked required arguments; an initial configuration left new admission disabled; an early fault fixture omitted its required Message input and produced native poison incidents instead of the intended fault. Those runs are not F1 evidence. The incidental missing-contract exception from the first fixture was then isolated/reproduced as F2 with a normally run legacy worker. `concurrent.log`, `single-control.log`, `policy-control.log`, `legacy-contract-final.log` and `cold-retry.log` are the final observations.

These are current-source-equivalent in-process product-path diagnostics plus a cold-process retry, not a current-head product rebuild, a fresh HTTP/Studio-host acceptance matrix, a full provider/CI run, or post-main evidence. No previous6165 pass is relabelled as evidence against these newly probed paths. Final `git status --short` is empty; no repository/GitHub edits, commits or writes were made.

## Dropped candidates and unchanged holds

A hypothetical start-replay resource-policy gap was not retained: normal replay is same-caller identity and the Execute permission is checked at the transport; no concrete authorized-policy bypass was demonstrated. No new model/native identity collision, destructive downgrade, owner-loss or terminal-parent failure survived the remaining checks. The six round2 corrections were examined as part of the complete diff, and their historical receipts retain their exact source limits.

The unanswered T042 API/version-magnitude card/needs:decision, owner-deferred GitGuardian39189012/neutral or size-skipped scan, exact final required CI, protected merge and post-main verification remain known delivery holds rather than fresh correctness findings. #2898/#2907/#2908/program remain open/Partial. #2735 remains delivered; #3242 and Orleans/task-runner deferrals are unchanged.
