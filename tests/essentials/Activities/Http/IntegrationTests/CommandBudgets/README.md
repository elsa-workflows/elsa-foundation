# EF command budgets

`EfCommandBudgetTests` runs three reference workflows on a fresh SQLite database under each checkpoint persistence
mode and counts the EF commands each run costs: every reader, non-query and scalar command the runtime's EF context
executes, counted by `CommandCaptureInterceptor`. A count above its budget in `ef-command-budgets.json` fails the
test. The counts are deterministic, so each budget is an exact baseline with no headroom. Nothing is timed.

| Workflow | What is counted |
|---|---|
| `writeline-sequence` | A Sequence of ten WriteLine activities, started directly and run to completion. |
| `http-reference` | One request to the four-activity HTTP workflow from [the delivery response, Q1](../../../../../../docs/reports/runtime-db-access/delivery-response.md): a Sequence of a synchronous POST HttpEndpoint, a Set computed in JavaScript, and a WriteHttpResponse. |
| `fork-join-resume` | A Parallel whose WriteLine branch completes and whose Event branch suspends on a bookmark, counted across the start and the resume. |

The counts cover the runtime's own SQLite database only. They are not comparable one-to-one with the PostgreSQL
request traces in `docs/reports/runtime-db-access`, which also counted persisted telemetry.

Lease and claim renewals and the EF schema gate's refresh, evaluation and backfill checks run on timers, so a run
slowed by a busy machine would count more of them. The test sets those periods beyond any run, which keeps timer work
out of the counts and the counts independent of wall time.

## Changing a budget

- Lowering a budget is a normal change. When your change reduces a count, lower the budget to the new count in the
  same pull request.
- Raising a budget needs a stated reason in the pull request that raises it.
- Every workflow needs a budget for every persistence mode. The test fails for a missing budget, and for a budget
  that names an unknown workflow or mode.

The rule lives here rather than in the JSON because `tools/quality-metrics/quality-metrics.sh` prints every top-level
entry of `ef-command-budgets.json` into the weekly metrics table.
