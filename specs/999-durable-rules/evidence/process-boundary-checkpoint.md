# Packaged PostgreSQL process-boundary checkpoint

This is a partial T019–T020 checkpoint for #3181. Five real-worker cases passed using the four-module package closure from product source `f923b12a41ab3280c146d2c1f8a45ba0f102caaa` and publish-state `eb3e12531b0cbd594374df1e49df0fadef9a50f3`. The prior [package checkpoint](package-checkpoint.md) describes those immutable package inputs. No product source changed or package was published for this checkpoint. Native composition, retained-run upgrades, live-successor stale new acceptance and separate persistence removal controls remain pending.

## Executed cases

Each case used a separate database in a disposable PostgreSQL 16.15 container. The consumer has PackageReferences only and resolves no Elsa Design or Definitions modules. An original GenericHost worker executes the actual evaluator and PostgreSQL EF store. A forwarding observer pauses the response only after the underlying store has committed and the durable state has been read back. Recovery starts another process against that same database without preparing or admitting the invocation again.

| Case | Original boundary | Observed successor and old-worker result |
|---|---|---|
| accepted-crash | Revision 2, zero consumed rounds, seed facts, pending receipt | Original killed while alive; successor succeeded with four rounds |
| consumed-crash | Revision 3, one consumed round, left/right/seed facts | Original killed while alive; successor succeeded with four rounds |
| accepted-delayed | Accepted response held after commit | Successor succeeded; released original observed Terminal |
| consumed-delayed | Consumed response held after commit | Successor succeeded; released original observed LeaseLost |
| accepted-cancel | Accepted response held, receipt unconsumed | Exact cancellation settled with zero rounds and seed facts; released original observed Terminal |

All recovery histories contain exactly ordinals 0–3 and final facts `final,joined,left,right,seed`. The first receipt ID, semantic hash, accepted attempt, worker, fence and accepted revision are preserved. Every recorded round has consumption evidence. Terminal IDs and revisions remain stable across repeated reads. For delayed cases, the coordinator also compares the successor's terminal bytes and revision against the original process after its callback.

The cancellation process suppresses only the Rules worker so it can exercise cancellation against the held pending receipt. A changed envelope first returns Conflict with no confirmation payload or durable snapshot change; the exact envelope then cancels and its replay leaves state unchanged. A separate invocation cancelled before admission creates no facts or receipts; subsequent admission returns AlreadyCancelled and changed-envelope cancellation returns Conflict.

The coordinator verifies marker and callback PIDs against the process it launched, requires a different successor PID, and checks every reported database against the database it created. Actual engine/version and container image are recorded. Connections stay in child environments and are redacted from logs. The owned container and its volume were removed successfully. This proves independent processes on one machine; it does not claim a multi-machine or power-loss experiment.

## Review, controls and limits

The first setup attempt encountered PostgreSQL's temporary initialization server shutting down before database creation. Inspection of the image entrypoint confirmed that temporary server accepts Unix-socket connections only. Readiness now probes TCP inside the container, which waits for the final server. That failed setup attempt qualified no process case.

Review strengthened database/PID attribution, required the precise Terminal or LeaseLost callback disposition, bounded successor execution, and removed an unsupported constant claiming exactly one terminal result. The final run includes those corrections. Probe compiler warnings were corrected. Root source review and the successful final run found no remaining defect within this checkpoint's stated scope.

The direct package cases and actual assembly-digest removal control ran again before the process cases: the intended identity assertion failed after removing the digest check, original source and deployed DLL bytes were restored, and direct execution passed again. This is the identity control only. It is not evidence for the still-pending cancellation-fingerprint, same-owner fence or durable-consumed-marker controls.

Post-commit delayed confirmation after terminal settlement does not prove rejection of a stale new acceptance while a successor holds a live lease. That distinct case and the three isolated persistence controls remain explicitly open in [tasks.md](../tasks.md). `processBoundaryProofPassed` is true; `fullAcceptancePassed` and `removalControlsComplete` remain false. Physical external effects remain at least once. These results do not explain historical unrelated timeout failures.

## Reproduction and evidence

```sh
python3 tools/rules/verify-packaged-rules.py \
  --stage process \
  --source-ref f923b12a41ab3280c146d2c1f8a45ba0f102caaa \
  --publish-state-ref eb3e12531b0cbd594374df1e49df0fadef9a50f3 \
  --evidence-dir /tmp/rules-process-evidence
```

The recorded run reused the already verified pack directory with `--packed-run /tmp/3181-packages-reviewed/run-5bfa6f869fe04035892d61add3b75e06`; consumer cache, deployment and databases were fresh. Final evidence is retained at `/private/tmp/3181-package-process-final/run-29a4994961964febafedaa1e65958f4f`. The earlier setup failure is `/private/tmp/3181-package-process/run-5104d7d4dba542059714413430ae7fd6`.

[process-boundary-qualification.json](process-boundary-qualification.json) preserves the final receipt, exact commands, package/harness hashes, actual outcomes and raw process artifact hashes. All 24 command-log hashes and current harness hashes were independently rechecked. There were 21 zero exits, one intended identity assertion failure and two intentional original-process terminations. The final harness remained unchanged during execution. Python syntax, diff and current generated-map/solution-filter checks passed. Product implementation and previously executed provider suites were not rerun or claimed as new evidence for this tooling-only checkpoint.
