# PR3410 complete-PR round 1 — acceptance lens

Reviewed head: `7cfbf739f0d4c3c220097918b04c2b33b5cb3bb3`. Scope: `git diff origin/main...HEAD`, merge base `c986f9ddfdec2c641b2f1c1f5d8f82f8dd19e529`; newer main's independent Studio slot change is excluded. Actual diagnostic products are `d7ee22b164bc04098bedde19e5b4722ed6f2b871`, matching this head's unchanged production implementation.

## Actionable finding

**[P1] Wait for downstream cancellation before settling an independently cancelled native worker.**

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnNativeWorkBoundary.cs:107-108` (the conditional call to `RequireCancellationSettlementAsync`).

The downstream settlement check runs only when the model already has an unsettled `Cancellations` entry for the worker. If the operator cancels the native RunEtl worker through the supported native CancelWorkflow operation first, the model has no such entry. Its authentic Cancelled result therefore passes validation while its authentic `Elsa.Etl.Cancel.v1` source is still pending. The driver records that result at lines217–224, excludes its already-recorded origin from `CancelRemainingWork` (lines331–332), and sets the direct model to `TerminalSettled/Cancelled`. An embedded model can likewise expose its terminal result to its parent without a cancellation entry for that worker. This violates FR013/SC003 and the supported cancellation contract's distinction between cancellation acceptance and downstream settlement.

This is a reproduced settlement-boundary failure, not a claim that the durable cancellation transport disappears or can never retry. During an ETL delivery outage/delay, the real ETL run can remain Running, with a delayed unfinished partition, after BPMN reports settlement. There is no shared outbox dependency barrier: intent-kind filtered delivery is supported; a failed item is recorded independently and does not block later items. The existing consumption witness explicitly delivers ETL cancellation first (`tools/bpmn/NativeWorkDispatch/Program.cs:267-273`), so its success does not cover this ordering.

### Decisive counterexample

Directory: `/tmp/elsa-2898-review-1/acceptance-cancel-order/`.

- `Program.cs` / `Probe.csproj`: small SDK diagnostic, direct references to the existing built production binaries; no repository projects rebuilt.
- `input-provenance.json`: original actual pending source is `evidence/pipeline/pending-cancellation/native-terminal/native-cancel/pending-elsa.sqlite.gz`, with its coupled actual pending ETL Runtime store.
- `result.jsonl`: full before/after source, worker, model and input receipt observations.
- `summary.json`: compact proof, source/product hashes, physical ETL rows and unchanged ETL-snapshot check.
- `build.log`: build succeeded, zero warnings/errors, 1.24 seconds.

The diagnostic makes fresh private copies of the original whole stores. It performs normal `IRuntimePostCommitOutboxProcessor.ProcessAsync` filtered to `Elsa.Bpmn.NativeWorkResult.v1`, then a normal model mailbox `RunSchedulerWork` drain. It does not seed/edit execution, queue, receipt or claim state. No ETL pump is run, representing cancellation delivery being unavailable while the independent result transport progresses.

Observed:

| Boundary | Before | After |
|---|---|---|
| Model | revision1, Running, no terminal | revision2, TerminalSettled, Cancelled |
| Native result receipt | absent | Accepted, revision2, lifecycle TerminalSettled |
| Exact ETL-cancel source | Pending | Pending |
| Coupled real ETL | Running; two accepted records; one incomplete delayed HTTP partition; no terminal | unchanged, byte-identical retained store |

All three inspected product informational versions name d7ee22b16, including the BPMN driver/boundary, Runtime and Runtime EF provider. Repository HEAD and clean status were rechecked after the diagnostic. No foreign host was stopped.

Fix the acceptance guard/state transition so every authentic Cancelled worker result with owed downstream cancellation cannot declare settlement or return to its native parent before that exact inventory is delivered. Add a failing regression for native-worker-origin cancellation with result delivery preceding ETL cancellation, plus actual source-bound recovery evidence for that reversed/pending ordering. Do not rewrite an already-terminal ETL result.

## Other acceptance disposition and limits

The consolidated FR001–022/SC001–006 mapping, spec/plan/contracts, actual partial-fork/Studio/legacy manifests and high-risk cancellation/retry/authorization/broadcast evidence were inspected against implementation. The main-adoption partial-fork bundle's 13 positive/corrupt controls passed independently; ten inherited Studio controls over the latest Studio bundle also passed independently, zero failures/skips. Recorded 32/32 hosting, 594/594 Architecture, six legacy families/22 original store inputs/33 resulting snapshots, paired fork8 models/24 stores and saved Studio4 runs/12 stores retain their executing-source labels; this review does not relabel them exact-head CI or claim fresh whole-suite execution.

No other concrete acceptance finding is raised. First-class direct-model Studio/operations, wider ETL/program delivery, non-SQLite actual-host upgrades, distributed/provider substitution, #3242 and Orleans/task-runner decisions remain their explicitly separate scopes. T042's owner version-policy card, needs:decision, GitGuardian/scan gate and exact-head review/required-CI/merge/post-main gates remain known delivery holds, not newly discovered acceptance defects. The issue/program must remain open/Partial until their remaining gates are satisfied.

This diagnostic is an SDK production-path falsification over actual captured store copies; it does not recreate an HTTP fixture or claim a new full owned-host qualification run.
