# Packaged process recovery and persistence controls

T019–T020 are implemented and verified. This checkpoint completes the missing live-successor and isolated-control evidence from the [earlier boundary checkpoint](process-boundary-checkpoint.md). The product packages still come from immutable source `f923b12a41ab3280c146d2c1f8a45ba0f102caaa`, publish-state `eb3e12531b0cbd594374df1e49df0fadef9a50f3`, and the same calculated eight-package closure. No product implementation, package publication or publish-state changed. Native composition, complete six-module packaging and retained-run upgrades remain pending.

## Process evidence

The final run repeated all five earlier boundary cases: original-process loss after accepted and consumed commits, delayed responses at both boundaries, and cancellation of accepted pending work. Each used an independent PostgreSQL database, actual GenericHost worker/evaluator, normal EF store and a clean PackageReference consumer with no design modules. Recovery preserved the original accepted receipt attribution, committed exactly four rounds and retained deterministic final facts. Delayed callbacks left the successor terminal bytes/revision unchanged. Cancellation identity, pre-admission cancellation and exact replay remained correct.

The added sixth case pauses the original real worker before forwarding its first acceptance. A distinct successor process acquires a new owner and higher fence at the same semantic revision, reads the same first-round input, and pauses before returning it to the evaluator. The coordinator checks that both processes are alive, the database/fingerprint matches, and complete public snapshots are identical. It then releases the old submission while keeping the successor held.

The old worker's actual AcceptRound, ReadWork and finally-Release calls each returned LeaseLost without exposing confirmation or successor payloads. The complete public run view, facts, round progress and terminal bytes remained unchanged. After the old process exited, the coordinator released the successor, which completed four rounds with first-attempt attribution to the successor's owner/fence and a stable terminal. Receipt IDs and semantic hashes agree with the deterministic candidate produced by the original evaluator; the original physical attempt was never accepted.

The paused successor renews through the real public store if needed, preserving owner/fence and semantic state. The final run needed zero renewals because coordination completed within the original three-second lease; it is not evidence that the renewal branch executed. Final successor fence was 2, model revision 9 and status Succeeded. The receipt records actual original/successor PIDs and live PostgreSQL 16.15 database/version/image. This is independent-process evidence on one machine, not multi-machine or power-loss qualification.

## Isolated persistence controls

Each control starts with a receipt produced and durably accepted by an actual worker, whose process is then intentionally terminated. A separate process exercises public store commands against that database with worker polling suppressed. The mutation applies only to the owned detached source clone and deployed EF assembly; it does not manufacture receipts or replace the evaluator/identity service. EF is dependency provenance rather than one of the three semantic package pins, and each deliberately changed EF asset is recorded as a control artifact, not a qualified original package.

| Control | Intact and restored behavior | Single removed statement/check and observed counterexample |
|---|---|---|
| Cancellation identity | Changed envelope returns Conflict with null confirmation and unchanged Running snapshot; exact cancellation and replay preserve only seed facts | Removing the stored fingerprint comparison causes the named changed-envelope assertion to fail |
| Same-owner fence | Two expiry/reclaim cycles retain owner and pending model revision 2 while fence advances 1 → 2 → 3; old-token Consume returns LeaseLost with no payload or snapshot change | Removing only `row.WorkFence == lease.Fence` causes the named stale-consumption assertion to fail |
| Consumed marker and replay | First Consume reports revision 3, exactly one round and left/right/seed facts; exact replay preserves transition/receipt/next-round IDs and original revision without mutation | Removing the consumed-model-revision assignment causes the named confirmation/marker assertion to fail |

All three intact baselines passed; all three changed versions failed their exact intended assertion; all three restored reruns passed. Original/restored source and deployed-assembly SHA-256 hashes match. Compilation or unrelated startup failure cannot satisfy a control. The consumed-marker control establishes durable consumption confirmation and historical replay, while the full process histories establish no duplicate logical advancement under the combined checks. It does not claim that removing that one assignment eliminates every other duplicate-prevention check.

The package identity removal control and its restored direct execution also ran again. Physical external effects remain at least once. Passing these cases does not establish a cause for historical unrelated timeouts.

## Review and validation

Two bounded review rounds completed. Root reviewed and integrated the C# probes; delegated review inspected Python orchestration. Review consolidated shared host composition and process lifecycle handling, strengthened full snapshot comparison and actual identity checks, removed an unsupported terminal-count claim, corrected stale evidence wording and made container cleanup run even when launch times out after creation. A separate mocked launch-timeout check verified propagation, cleanup of the exact generated container name and no qualification; it is explicitly not a real Docker timeout experiment. The second focused review found no new actionable issue.

The final real run exited zero with an unchanged harness and successful owned-container/volume removal. All 47 command-log hashes, current harness hashes and restored source hashes were independently verified. Its command outcomes are 32 zero exits, four required assertion failures (one package identity plus three persistence controls), and eleven intentional original-worker terminations (two crash cases plus nine control preparations). There were no C# compiler warnings. Existing dependency audit warnings remain visible and were not suppressed or remediated here. Python syntax and diff checks passed. The current generated-map/solution-filter check passed; prior product/provider/architecture suites are not claimed as newly executed for tooling-only changes.

## Reproduction and durable receipt

```sh
python3 tools/rules/verify-packaged-rules.py \
  --stage process \
  --source-ref f923b12a41ab3280c146d2c1f8a45ba0f102caaa \
  --publish-state-ref eb3e12531b0cbd594374df1e49df0fadef9a50f3 \
  --evidence-dir /tmp/rules-process-evidence
```

The actual final command also supplied `--packed-run /tmp/3181-packages-reviewed/run-5bfa6f869fe04035892d61add3b75e06` to reuse unchanged verified archives. Its consumer cache, deployment and databases were fresh. Final run: `/private/tmp/3181-process-controls-final/run-a9c82057b9784aa7821de7b3c8c1ec73`; the earlier pre-review integrated run also passed at `/private/tmp/3181-process-controls/run-efecc822e7d4460aab94996c925dda60`.

[process-qualification.json](process-qualification.json) preserves the final receipt, exact commands, package/harness/asset identities, results, raw artifact hashes and cleanup-control outcome. `directProofPassed`, `processBoundaryProofPassed` and `processProofPassed` are true. `fullAcceptancePassed` remains false. These task completions do not close #3181 or the broader program.
