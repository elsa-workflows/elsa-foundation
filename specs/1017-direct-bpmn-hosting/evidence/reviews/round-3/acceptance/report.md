# PR3410 author review — round 3 of 6 — acceptance lens

Reviewed exact head: `fcf551c789c37c722b3b07d0c1cf4aecb56a16ad`.
Main merge base: `184759ce53bc251ccc81b07ec3980d804ad8fd76`.

This independent read-only review has **two P2 acceptance findings**. No current-round reviewer output was read. The repository remained clean and at the pinned head. No repository, GitHub, user-host/tab/store writes, or execution/queue/receipt/claim/clock seeding occurred. Own scripts, logs, private read copies and the stateless probe are confined to `/tmp/elsa-2898-review-r3-acceptance`.

## Findings

### [P2] Retain context when a timed model has an unsupported human binding

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs:395-398`.

FR019 requires unsupported constructs/bindings/versions to be refused before admission **with useful process/element context**. The round2 correction fixes the preceding unsupported-element predicate, but the timed binding-inventory predicate still discards its offending element. An otherwise supported format4 process `approval-process`, containing user task `approval-task-42` bound to `approval-work-42` and its interrupting duration timer, is accepted with its complete inventory. Change only the inventory entry to `unrelated-work`, leaving the task binding unchanged: production Graph refuses it with `The duration profile requires explicit human work or one closed native service, with exact timer bindings.` The process, element and missing binding are absent. The API renderer forwards this message as HTTP422 (`BpmnHostingApi.cs:110`), so the caller cannot locate the bad binding in a larger submitted process.

The complete-inventory control and the single-change refusal were independently executed against the actual standard Workbench Release BPMN product labeled `e3ec219fcc3a93c949d2669fc585bd1e79152abe`; its hash matches the new journey receipts and the driver source is byte-identical to the reviewed head. See `validation-probe.log`, `validation-probe-provenance.json` and `validation-probe/Program.cs`. This is a stateless diagnostic proof, not a new HTTP-host or recovery witness. Retain the offending timed task/binding when validating the inventory and include its process/element/binding in the domain refusal, with a behavioral regression. No admission or persisted-state corruption is alleged.

### [P2] Qualify pipeline-originated cancellation through embedded RunBpmn

Location: `specs/1017-direct-bpmn-hosting/evidence/qualification.md:52` (also the FR013 mapping at line35).

The SC003 row treats direct/embedded cancellation coverage as complete, but the specifically required **pipeline-originated cancellation** is demonstrated only directly. FR013 and SC003 (`spec.md:124,151`) explicitly require direct/embedded outcome qualification. The sole actual receipt with `pipelineCancel=true` is `pipeline/direct-cancellation/direct-cancel/process.json`, source `b5c612276334d1d39aec827b85d12cb1021e5c38`, with no embedded parent or native-parent execution. It calls the normal ETL cancel API after recovery and proves typed ETL `Consumed` delivery into a live RunEtl worker and direct BPMN.

The embedded success/cancel matrix instead uses `nativeParentCancel=true`; the five inverse-order cases use `nativeCancel=true` and `pipelineCancel=false`. Those cancel native state first and permit ETL result delivery as `ProvenParentTerminal`. They do not exercise the distinct ETL-originated typed cancellation through the still-live RunEtl worker, model and original native RunBpmn parent. The FR013 index also labels `pipeline/native-cancellation/README.md` as pipeline-originated proof, although that receipt explicitly calls CancelWorkflow on the native worker and distinguishes itself from the ETL API case.

Independent audit of all140 retained process receipts under pipeline/races/events/retention found exactly one pipeline-originated case and zero embedded pipeline-originated cases; see `pipeline-origin-audit.json`. This is a missing required actual-host acceptance case, not a reproduced implementation defect or a request for broader #2908 connector/upgrade work. Exercise the ordinary ETL cancel API for a real embedded RunBpmn→RunEtl journey after process loss, retain exact typed ETL/native/model/parent consumption and settlement/ACK plus remaining-work/effect disposition through another process, and update the evidence mapping. Keep this arm incomplete until that evidence exists.

## Full scope and discussion

The full comparison contains6479 changed paths,6081 under feature evidence and398 elsewhere. Reviewed the whole change inventory and targeted production/test/tool surfaces across model material/hash/incarnation/dependency retention; optional checkpoint/input/fence/queue/marker participation; protocol/lifecycle selection; supported API and authorization; native work admission/retirement; typed ETL results and cancellation settlement; RunBpmn parent admission/return; human/timer/signal profiles; legacy interpreter extraction; Runtime and Design migration/history/tooling changes; owned host fixtures/controllers/readers; public profiles and qualification manifests. Generated migration/lock/evidence families were assessed through their physical/provider/history/restore evidence rather than a binary-heavy textual diff.

Read the spec, plan, tasks, hosting contract, quickstart, review-notes and FR/SC index; reconciled all78 issue2898 comments and193 PR comments, including114 identical CLA posts and mirrored issue/PR bodies. Supplied reviews, inline comments and threads are empty. Checked program2892's body and the owner-approved continuation/direct-recovery-first/current-goal comments. Historical round1/2 receipts were considered without reading any other round3 reviewer output. #2735 remains delivered; #3242 native fault projection and Orleans/task-runner deferrals remain unchanged.

## Requirement disposition

All positive assessments below retain their actual source, built-in single-node/SQLite and supported-profile bounds.

| Requirement | Assessment |
|---|---|
| FR001 | Own direct model identity and model-owned progression; actual Runtime-only captures have no native enclosing execution/activity/bookmark rows; shared released0.5.0 interpreter supports RunBpmn. |
| FR002 | Paired partial-fork, service and timer traces compare after declared producer-handle normalization; raw invocation/attempt identities remain checked independently. |
| FR003 | Semantics remain in BPMN; native composition keeps existing parent-specific fault/cancel policy. |
| FR004 | State/input/queue/outbox/fence/final marker share the physical commit; provider rollback/CAS controls are distinguished from actual-host proof. |
| FR005 | Exact recipient receipts precede ACK; pending return, consumed-before-ACK and sibling cancellation sources survive process loss; physical HTTP effects remain at least once. |
| FR006 | Actual native-admission and held-command races retain successor fences and refuse stale decisions; obsolete physical attempts cannot progress the model. |
| FR007 | Supported API authorization precedes retained-outcome disclosure; exact retry/conflict/foreign identities and obsolete results are qualified. |
| FR008 | Real partial fork SIGKILL/restart and same-model direct/native pairs preserve accepted left progress and one logical join/terminal result. |
| FR009 | Both human/timer and worker/timer orders survive restart, with old inputs unable to advance the winner. |
| FR010 | Actual delayed-first-selection/populated/empty/rearm cuts retain the first delivery cohort; later/rearmed listeners are excluded from retry. |
| FR011 | Actual inspect/complete/cancel/incident/retry commands use supported boundaries and durable identities; the repaired invalid human retry no longer blocks later completion/cancel. |
| FR012 | Finite HTTP ETL journeys retain actual retry/output, native/model/ETL pins and one logical admission through recovery. |
| FR013 | Success, matched/unmatched business errors, fault and propagated cancellation have substantial actual proof; embedded pipeline-originated cancellation remains the second finding. |
| FR014 | WaitingIncident preserves active pre-failure semantics, one distinguishable bounded replacement and original fault evidence; replay, old-result, final/budget/cancel refusals are covered. |
| FR015 | Root/Sequence/Flowchart/CompensationScope outcomes and pending native-parent return retain their distinct policy; #3242 is not settled. |
| FR016 | Actual pre-writer captures,16 writer hashes across three sources and22 original stores verify; actual legacy and both Design histories recover through product commands. |
| FR017 | All104 frozen assets are byte-identical to GateA; authored/published material, original migrations, full pins and absent-participant fingerprints retain compatibility paths. |
| FR018 | Both actual saved Studio families retain open/edit/export/save/publish/Published Run/Test Run/inspection/restart proof at4f46. Relevant legacy/authoring/Design paths remain unchanged; no current-head browser or direct-model Studio acceptance is inferred. |
| FR019 | Unsupported new work is refused and old supported material remains recoverable; timed binding refusal context remains the first finding. |
| FR020 | Actual retired-publication/GC/admission-off Runtime-only recovery retains original owed pins with Design unavailable; predecessor-reader refusal is a bounded reader witness. |
| FR021 | Profile, operation, XML/DI, provider and broader-program limits remain explicit. |
| FR022 | Source-bound hosts, stores, controls and prior failed diagnostics remain separately labeled; the missing SC003 origin/embedding combination must not be inferred from neighboring cases. |

SC001 fork/equivalent traces, SC002 races/cohort, SC004 supported operators and SC005 saved-state/Studio/frozen compatibility are supported within the recorded bounds. SC003 has the second finding; SC006's mapping needs that correction. The five issue criteria map respectively to released-library/frozen compatibility, fork/races, real pipeline outcomes, minimal commands/equivalent traces and declared cuts. Criterion3 retains the embedded pipeline-origin cancellation gap; the narrow FR019 diagnostic gap also prevents a clean acceptance round. Existing unchecked issue rows and delivery holds remain appropriate.

## Independently verified evidence

- Portable suite:397 tests across30 evidence-control modules plus the current adopted-template Studio override; zero failures/errors/skips (`portable.log`). These controls validate actual artifacts; they are not substitute runtime executions.
- New round2 actual journey verifier: four immediate-completion direct/native-parent cases,16 whole stores/eight Runtime–Design pairs,12 distinct killed owned hosts and24 rejected observation-copy controls. The first accepted embedded input is revision1/TerminalPendingSettlement; the exact parent consumes the return and revision2 settles. Parent waits, full pins/incarnations and stable third-process observations are checked.
- Manifest/Git/SQLite audit (`hash-audit.json`):104 baseline assets/22 stores,81 new qualification assets/16 stores, seven adopted-main assets,39 migration-repair assets/six final stores and92 current Studio assets/12 stores; all323 assets match exact committed Git blobs, all56 whole stores pass read-only integrity checks. Sixteen writer hashes match all three capture sources (48 comparisons); all104 frozen assets match GateA6af6df2c bytes.
- Physical Design history readback (`design-history-readback.json`) preserves the original September row plus the observed cumulative October row for legacy input, and only the original October row for the actual October writer; no fabricated September row is present in that writer's final store.
- Source audit confirms all BPMN/Runtime/shared-EF/Design production trees are byte-identical frome3 to this head. Six explicit Studio compatibility ledger files match4f46; the CLR-only helper renames and new direct-model paths are distinguished. Adopted main3480 adds the declared connection/SDK error behavior, with its own157 Connections/34 SDK checks and no new browser claim.
- Independently reconciled committed discovery logs against the original closed local e3 TRXs: BPMN465, Runtime3078, hosting37, RuntimeEF1782, Architecture594 and native-provider209;6165 passes, zero skips, every discovered method accounted for, with declared runtime/architecture theory expansion (`whole-project-reconciliation.json`). This is original source-qualified evidence inspection, not a new test run or final-head CI. New raw red/green regressions and the actual Root red diagnostic retain their correct before/fixed sources.
- Viewed three current saved Studio screenshots independently: five-node saved layout, restarted completed Published Run and restarted completed Test Run, including their explicit run kinds. No live user UI was accessed.
- Stateless current production probe independently proves finding1, with an accepted complete-inventory control; separate receipt inventory proves finding2. No new full HTTP host, provider-container suite or CI run was performed by this reviewer.

The initial reviewer-only in-memory SQLite integrity attempt refused WAL-mode snapshots; it was replaced by read-only immutable file connections to exact private copies. Original evidence files were unchanged. Earlier runtime/ETL/upgrade receipts retain their original sources. The whole37-host repeat includes old families/Design histories but duplicates of their stores remain local; only the16 new immediate-completion stores are portable in the new bundle, as declared.

## Existing holds and remaining program work

These are not new findings: unanswered T042 API/version magnitude card and needs:decision; deferred GitGuardian39189012 and neutral/size-skipped scan; final exact reviewed-head required checks; protected merge and resulting-main verification. A clean review or merge eligibility is not asserted. Actual non-SQLite upgraded hosts, upgraded-host/wider ETL, first-class direct-model Studio/operations and remaining #2907/#2908/program2892 work remain separate. The two findings above are inside spec1017's declared acceptance and do not reopen the settled authorization question.
