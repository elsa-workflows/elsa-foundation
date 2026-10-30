# PR3410 complete-PR round 5 — independent correctness review

Reviewed head: `1e707d58fc2148be3715d50616dfdb69dc82ae65`.
Base: `7b49513650665606e5f52fdc08c5831f60802dac`.
Starting receipt: https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6535744309.

Verdict: **one actionable P2 correctness finding; this lens is not clean.** This is round 5 of the existing six-round bound. No other current-round reviewer output was consulted.

## P2 — Release a fully settled model's in-process mailbox

Primary changed line: [`RuntimeExecutionProtocolRouter.cs:57`](https://github.com/elsa-workflows/elsa-foundation/blob/1e707d58fc2148be3715d50616dfdb69dc82ae65/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/RuntimeExecutionProtocolRouter.cs#L57).
Related changed line: [`WorkflowSchedulerDrainer.cs:220`](https://github.com/elsa-workflows/elsa-foundation/blob/1e707d58fc2148be3715d50616dfdb69dc82ae65/Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowSchedulerDrainer.cs#L220).

Every model drain deliberately returns false from `IsWorkflowTerminatedAsync`, including a model whose authoritative lifecycle is already `TerminalSettled`. This protects late settlement/rejection inputs from the native terminal stop policy. However, the router then projects that drain unchanged through `WorkflowExecutionCommandProcessResult.FromDrain`. Consequently `WorkflowTerminated` is always false for a model, and the built-in actor provider never sees the terminal metadata which triggers its enabled-by-default mailbox eviction. The existing resumption reaper cannot repair it: `RuntimeResumptionService.IsTerminalAsync` reads only native execution headers, and direct model executions intentionally have none.

Concrete failure: a long-running qualified built-in host that handles a stream of valid direct BPMN executions retains one mailbox, lifecycle lock and bounded-but-per-execution idempotency cache for every execution ever completed. Even a plain start → end model with no native workers, timers or pending settlement accumulates these objects permanently. There is no idle/LRU eviction in this provider; `RuntimeActorEvictionOptions` explicitly describes its policy as terminal eviction only. More completed work therefore creates unbounded process-memory retention despite `PassivateOnTerminal=true`.

Expose model terminality to the mailbox cleanup decision after the permitted input drain finishes, while keeping model input admissibility separate from the native stop/purge decision. Simply returning true at the start of the current native termination guard would prevent required post-terminal inputs from being consumed and is not a suitable repair. Add a regression that completes many distinct direct executions, verifies the live-mailbox gauge returns to its baseline, and still proves pending settlement and exact retained replay work after reactivation.

### Executed counterexample and control

`mailbox-probe/Program.cs` uses the actual compiled `ActivitiesBpmnHostingFeature`, public `IBpmnModelRuntime`, built-in actor provider, EF SQLite checkpoint/material/queue providers, runtime ownership and released BPMN interpreter. It uses the repository's existing `WorkflowExecutionHarness` only to compose that production graph, as the existing direct-driver tests do. It does not stub the driver, decision result, state reader, ownership or actor provider.

The final run (`mailbox-probe/completed-models.log`, `mailbox-probe/completed-models/result.json`):

1. Enable the supported hosting feature and leave the registered `RuntimeActorEvictionOptions.PassivateOnTerminal` at its actual default, true.
2. Publish one valid format-1 start → end model through `IBpmnModelRuntime.PublishAsync`.
3. Start eight distinct executions through `IBpmnModelRuntime.StartAsync`. Every outcome is consumed, `Completed` and `TerminalSettled`; every model input queue is empty.
4. Observe the provider's public `elsa.runtime.actor.live_mailboxes` gauge increase from **0 to 8**, exactly one per completed execution.
5. Re-drive every completed execution with an ordinary `RunSchedulerWork` command. All commands are Accepted, all terminal-dispatch metadata is absent, and the gauge stays **8**.
6. As a separate diagnostic control, explicitly invoke the existing `IWorkflowExecutionActorProvider.PassivateAsync` port at the checkpoint boundary for those exact eight executions. The gauge becomes **0**. Replay all eight original public starts: all return their exact consumed retained outcomes with `Replay=true`, `Pending=false`, and the gauge remains **0**. This is an explicit cleanup control, not an implemented product fix or a claim that automatic eviction passed.

The closed final SQLite backup has **8 model headers, 8 input receipts, 0 native execution headers and 0 scheduler items**, with `integrity_check=ok` (`mailbox-probe/closed-counts.json`, `mailbox-probe/snapshot.json`, `mailbox-probe/completed-models-closed.sqlite.gz`). The backup was taken only after the producer exited; no persisted state, receipt, clock or authority was seeded or modified by SQL. A read-only immutable SQLite connection was used after plain read-only mode could not open the closed WAL-mode database without its removed sidecars.

The first probe setup lacked its copied SQLite native library; that setup-only failure is retained. Initial diagnostic runs included a native harness run and are retained under `observed`/`observed-final`; they are not used as a positive native-eviction control. The final model-only run starts with zero mailboxes and zero native headers.

## Independent verification and source boundary

- Whole BPMN test assembly: **481/481 passed, zero skips**.
- Focused Runtime EF model checkpoint, admission lease, queue-format and migration tests: **50/50 passed, zero skips**.
- Probe build: zero warnings/errors after correcting two probe-only compile errors; final producer exits 0. The retained results are not a production-source red-to-green repair.
- The existing test/probe products report source `f6d3c370e5c3295bb9ea0499b0ea6adbc533b8a1`. Git comparison verifies **all 95 changed product C# files within the implicated BPMN/Runtime/shared-EF trees** have identical blobs at that source and the reviewed head. The complete relevant test trees are also byte-equivalent. `provenance.json` binds the source comparison and TRX counters; `products.json` records the binary hashes, which were unchanged after testing. These tests are not relabelled exact-head CI.

Review covered the entire production diff, including shared material identity/dependency retention, lease acquisition and root-write fencing, model admission/state/receipt CAS and native-parent conditions, queue schema/readability fences, protocol selection, terminal and obsolete inputs, human/timer/retry handling, delivery-time signal cohorts through real native Event bindings, service/ETL worker admission/results/cancellation, RunBpmn exact parent consumption, coalescing, actual-old-fault automatic recovery and migration-history compatibility. Test/tool/evidence changes and previous-round source-bound corrections were supporting inputs; earlier findings were not assumed exhaustive.

Current org protocol, repository AGENTS/contributing/review requirements, relevant constitution/glossary and spec1017 contracts were considered. The supplied paginated conversation input set contains **93 issue2898 comments, 230 PR3410 issue comments, 0 submitted PR reviews and 0 inline review comments**, through the round-5 starting receipt; program2892, coordinating2907 and ETL2908 context is also included. `conversation-index.json` records all646 comments across those five conversations. Historical review findings, corrections and explicit remaining owner decisions were considered. Repetitive CLA requests are not signing authorization. The owner's compensation-program note preserves its existing logical-operation-key ownership; this PR introduces no competing effect key.

Existing owner API/version-magnitude decision (`needs:decision` on issue/PR), GitGuardian39189012 deferred with Handle it later, exact required CI, branch protection/protected merge and resulting-main qualification remain **open delivery gates**, not additional new code findings. No human hold was treated as withdrawn or overridden. Broader program/direct-model Studio/non-SQLite actual-host qualification remains separate.

I did not rerun process-host/Studio journeys, external provider tests or whole-solution tests in this review. Their committed evidence retains its original executing-source limits. No GitHub writes, repository edits, commits, branch changes, product rebuilds, user-host operations or browser actions were performed. Only temporary probe/test outputs were written; the worktree remains clean.
