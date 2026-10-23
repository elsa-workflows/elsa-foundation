# Integrated compatibility checkpoint

The [receipt](final-compatibility-receipt.json) retains command records, log/TRX hashes and counters
for the completed local checks. These are local branch results, not exact-head hosted CI or
resulting-main evidence. Full leaf acceptance remains false.

Subsequent Code Review found that bookmark-resume boundaries disabled coalescing for the rest of a drain. The [coalescing review repair](coalescing-review-repair.md) supersedes the earlier clean-review conclusion for that path and records fresh source-built verification. The later [visibility and command-budget repair](coalescing-visibility-repair.md) records the CI failure and provider-capability correction. The historical suite results below retain their original scope.

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Rules, before the final two native cases | 276 | 0 | 0 |
| Rules, with the final two native cases | 278 | 0 | 0 |
| ETL | 1,009 | 0 | 0 |
| Activities runtime | 662 | 0 | 0 |
| Workflow runtime | 2,540 | 0 | 0 |
| Workflow runtime EF | 1,003 | 0 | 0 |
| Architecture | 585 | 0 | 0 |
| Workbench, full instrumented process suite | 62 | 0 | 0 |

The Workbench application build completed with zero errors and 85 warnings. Architecture checks
were rebuilt after that application build. The current generated solution-filter check passed;
it does not imply that an obsolete committed map snapshot was regenerated. Independent read-only
review found no concrete regression in the shared scheduler deferral, retained claim or coalescing
changes. Those results accompany the executed runtime suites above.

The local branch contains a normal merge of main `5d1df7097db3a731330735a94c2b6a5fb7f9a476`.
That integration added only the 21 Studio/spec files from #3093 and required no conflict resolution.
The ETL run overlapped that source merge; its backend inputs did not change.

## Workbench process-suite qualification

The earlier full suite returned 61 passed and one timeout in
`Original_uncancelled_checkpoint_is_rejected_after_a_real_successor_acquires`. The isolated rerun
passed. The logs showed SQLite contention while startup finalized a schema operation, but neither
the lock holder nor a causal chain to the missing acquisition event was established. The gated
checkpoint had not opened its EF transaction at the observation point; attributing the lock to
that specific checkpoint would be incorrect.

The test-only witness now records selected successor acquisition entry, return and exception type.
If that scenario fails, its fixture still stops its owned processes but retains the database and
journal for inspection. No runtime behavior, lease policy, timeout or assertion was weakened.
The full instrumented rerun has now completed: 62/62 passed, with zero failures or skips
and exit code 0. The receipt records the exact counters and log/TRX hashes. This
later pass does not prove the historical timeout's cause.

## Remaining gates

Packaged held-sibling/join, cancellation/unavailable recovery and invalid-input journeys passed
separately; see [final packaged native acceptance](native-package-final.md). The final integrated PR review,
exact-head required CI and resulting-main verification remain open. Four-provider, actual-package,
separate-process, native-installation and retained-deployment evidence keep their own scopes and
source identities; they are not relabelled as executions of this local test command set.
