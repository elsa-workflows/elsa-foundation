# PR3410 full-PR round10 correctness review

Verdict: **one P2 static correctness finding for root adjudication; no clean verdict**. The failure path below is established by supported production code. There is no executed reproduction in this round, and none is implied.

Exact reviewed head: `5ff0d52c6245ab89bc673e45da683a0f6c3cc141`. Adopted base: `e26c11bd94bc7a99d6cd0c522ada89961ef72aa5`. Final live read-only GitHub response confirms those SHAs and open/draft state (`final-remote-head.json`); local HEAD/origin/main match and the shared checkout is clean. No shared source edits, GitHub writes, pushes, host interruption or subagents. No other round10 reviewer output was read.

## C1 / P2 — Do not make cancelled model settlement wait for an obsolete future native scheduler continuation

Primary location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnNativeWorkBoundary.cs:136-137`.

The new physical scan is broader than prior descendant cancellation responsibility: it refuses the Cancelled native result until **every** retained non-result source is Delivered. A supported native CompensationScope backoff leaves a future EnqueueSchedulerWork source physically Pending. An ordinary whole cancellation terminalizes that worker and cleans activity-owned bookmarks/timers/queued work, but it does not retire an outbox source that has not reached the queue. The future source can be delivered only at its existing NotBefore. The new guard consequently prevents the model from accepting the worker's real Cancelled result until that due time, even though the worker is already Cancelled and the delayed continuation will acknowledge/purge without executing its child. With a one-hour handler backoff, model cancellation remains pending for nearly an hour; the supported default host cap permits24 hours.

Concrete supported ordering: a format2 BPMN service binds a published closed-input native CompensationScope; its forward WriteLine succeeds, its next forward Fault starts compensation, and its Fault handler fails with attempts left and a positive retry delay. Cancel that BPMN execution while the next compensation attempt waits, then run the existing outbox/actor recovery. This uses only built-in activities and existing public model/actor commands. The publication validator accepts this fully pinned worker shape. Existing spec261 BK4 tests already identify native cancellation during the retry wait as supported behavior. This is neither arbitrary BPMN token alteration nor a manually seeded invalid frame.

The complete line-by-line producer/cancellation/due-delivery/settlement proof is `static-delayed-retry.md`. Principal supporting locations:

- CompensationScope.cs708-731 and941-955: a real failed handler schedules the next attempt with now+delay.
- SchedulerWorkHandlerHelpers.cs102-120 and RuntimePostCommitOutboxItems.cs46-56: native-owned EnqueueSchedulerWork, Pending with future AvailableAt. RuntimeCheckpointCommitter.cs238-241 excludes it from immediate hopping.
- WorkflowCancelSchedulerWorkHandler.cs87-115; WorkflowCancellationPlanner.cs78-90; ActivityScopeCleanupStore.cs23-58: cancellation cleanup covers bookmarks, durable timers and actual scheduler queue work, not pending outbox sources.
- EfRuntimePostCommitOutboxStore.cs203-214: new lookup returns this Pending source despite its future due time. Lines901-907 and1124-1128: ordinary transport cannot deliver it before AvailableAt.
- WorkflowScheduleActivitySchedulerWorkHandler.cs91-99,174-188: the delayed child is obsolete after physical native cancellation and cannot execute its compensation body.
- BpmnNativeWorkDelivery.cs62-70: the model's own cancellation source cannot acknowledge before that authentic result is accepted.

Keep round9's genuine prior cancellation proof, including old already-queued results, but distinguish that obligation from obsolete delayed native scheduling. Existing physical provenance can help: CreatePending retains commit.Metadata, and the actual round9 earlier CancelEmbedded source has CheckpointReason=ActivityScopeCancellation plus ScopeCancellationReason. Do not simply allowlist only that marker: structural subtree cancellations retain ScopeCancellationReason on activity state while the outbox source carries the encompassing callback commit metadata. Preserve those genuine obligations and the source's scope/identity/delivery proof. No source/result rewrite, fake Delivered state, replacement cancellation authority or new model command is proposed.

**Evidence limit:** this is static proof of the current production branch interaction, not an observed running/cold host failure. An attempted private producer failed during helper compilation; no runtime scenario ran. The incomplete helper and all setup logs are preserved. Root should adjudicate the source trace on that basis, and retain a real queued-result/input cut before any writer repair if it elects to reproduce.

## Review coverage and reconciliation

The full current PR, beyond the last correction, was reviewed/reconciled across API authorization and replay; graph/profile restrictions and released interpreter decisions; direct human/service/signal/timer/retry outcomes; native admission, worker results, cancellation, embedding and retained legacy faults; scheduler FIFO/claims, shared ownership and recovery; atomic state/input/outbox/checkpoint and receipt/CAS boundaries; full material pins/incarnations, dependency closure, root-write leases and GC; runtime schema/upcasters and retained Design migration lineage; compatibility constructors; composition/project/tier/tool wiring; specs and source-bound qualification. Studio production source has no remaining diff from the adopted base. No additional concrete correctness defect was confirmed.

Round9 C1 is corrected for the authentic earlier scope-cancel source and its already-queued result. Physical source reads are bounded and validate payload/projections before status/kind exclusion. Existing actual queued-result recovery preserves old result bytes, authority, pins/incarnations, native terminal state and cancellation-before-decision-before-ACK order; none is relabelled as a new round10 execution. Earlier round8 shared-parent-ACK cleanup remains available. Completed/Faulted workers retain their actual terminal truth. Scoped preview API and GitGuardian decisions remain settled, and broader direct-model Studio/ETL/provider/program acceptance and deferred work stay open.

Current discussion exports confirm341 PR comments,131 closing-issue comments,326 program comments,17 issue2907 comments,30 issue2908 comments,0 submitted reviews and0 inline comments. Existing full-history discussion was reconciled with the refreshed bodies and all new/updated records, including launch6547225864 and the owner-authorized bounded continuation. The only old PR comment updated since prior review is the CodeRabbit skipped-review notice, which was read. Identical bodies and repeated CLA notices were deduplicated. Current org/repo/Studio protocols and applicable contributor/constitutional rules were reconciled. `discussion-receipt.json` retains the exact cache hashes; `new-updated-discussion.json` retains the incremental body audit.

## Verification and exact source limits

Fresh independent read-only checks:

- All156 correction-qualification assets and supplied decoded hashes verify; all104 frozen baseline assets verify unchanged.
- Changed production source diff-check passes. Shared checkout remains clean.
- Production source and BPMN test paths are identical from source43741654e through reviewed head5ff0d52c6. Release Bpmn/Runtime/Core/RuntimeEF/test-support files identify43741654e; native composition/Resumption files identify36831a014.437 is an ancestor of368, so those host products already include projection hardening. The file versions/hashes in `referenced-release-products.json` are inspected metadata, not newly executed product evidence.
- Final live GitHub head/base read matches the review freeze.

No new behavioral tests passed or failed in round10. Three private helper bootstrap compilations fail separately in `build.log`, `build-2.log`, `build-3.log`: missing module references; missing Resumption reference; then local type/import/constructor/request signatures. No product scenario or regression assertion executed. Further builds stopped under the org rule and parent's explicit instruction; the report completes through read-only review. These are setup errors, not a negative control for C1 or a product failure. The original whole Runtime/ETL Runtime/ETL Design inputs were copied before the planned probe and remain unchanged; no new counterexample input capture is claimed.

Prior executing evidence remains at its declared source: whole BPMN507 and RuntimeEF1811 at437; six affected standard old-fault/queued-result hosts at368; whole hosting45 and Runtime3083 atd21; architecture594/API15/maps/tiers and browser59/frontend2812 at the recorded sources. Actual saved Studio remains b7/four runs/twelve stores, with zero model executions/inputs in those native compatibility stores. No new live Studio, ETL, other-provider or broader acceptance claim is made. Exact-head CI, protected merge and resulting-main verification are separate delivery gates.

Exact commands/setup limits: `commands.md`. Static failure and metadata detail: `static-delayed-retry.md`.
