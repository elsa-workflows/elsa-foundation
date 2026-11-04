# Checkpoint savepoint reduction: measured result

Base production source: `e4446f73c5ce907bcc07067ee4e3f54b2d0d2a1c` (2026-11-04). Program #2531, bounded DB-cost lane #2564. The candidate changes only automatic savepoint handling inside the owned EF checkpoint transaction. Both participant and marker saves, their order, commit count, execution fences, lease/admission outcomes and immutable-marker reconciliation remain.

## Measured database cost

Release .NET10, native workflow runtime, PostgreSQL16 (`postgres:16-alpine`) through the existing command-cost wire relay; existing composed TestServer runner. Balanced default, max50 activities per checkpoint. SQLite uses the identical reference runners. Host startup, migrations, publishing and assertion queries are excluded; complete start/drain work and settlement are included. The runner checks Completed, empty scheduler backlog, delivered outbox and zero incidents. No work is shifted to a background phase.

| Workload | Mode | PG EF commands | Logical EF statements | Checkpoints | Transactions | Created savepoints before→after | Wire round trips before→after |
|---|---|---:|---:|---:|---:|---:|---:|
| Bare WriteLine, one activity total | Balanced | 46 | 50 | 1 | 2 | 3→1 | 54→50 |
| Sequence of one WriteLine, two activities total | Balanced | 48 | 54 | 1 | 2 | 3→1 | 56→52 |
| Sequence of ten WriteLines, eleven activities total | Balanced | 66 | 90 | 1 | 2 | 3→1 | 74→70 |
| Sequence of ten WriteLines | Durable | 209 | 269 | 12 | 15 | 27→3 | 278→230 |
| HTTP reference, four activities total | Balanced | 78 | 97 | 2 | 4 | 6→2 | 94→86 |
| Fork/join/suspend/resume reference | Balanced | 135 | 158 | 5 | 8 | 13→3 | 169→149 |
| Suspend then100 WriteLines reference | Balanced | 387 | 625 | 8 | 15 | 23→7 | 448→416 |

All72 runs (36 per provider) preserve EF command counts, the ordered per-command logical statement lists, checkpoint markers and transaction counts. Every checkpoint removes two automatic savepoint creations and two releases: four database statements and four PostgreSQL wire round trips. The separate root-write-lease transaction retains its own savepoint. The rollback probe still exercises actual savepoint creation and rollback, plus transaction rollback accounting outside the checkpoint path. SQLite command counts stay unchanged: ten WriteLines remain90 Balanced /269 Durable; the under40 target remains unmet.

The one-activity Balanced case removes7.4% of wire round trips; ten leaves removes5.4%, Durable ten leaves17.3%. These are transport-count reductions, not measured throughput or latency improvements. Commits and their flush costs remain. The bounded nine-line production change earns its place by removing deterministic transport work without a new cache or persistence protocol.

## Attribution and remaining cost

The Balanced WriteLine series (1/5/10/20/40 leaves, plus Sequence) is exactly `2 × leaves +46` PostgreSQL EF commands and `2 × leaves +54` wire round trips before, `2 × leaves +50` after. Savepoint suppression removes fixed per-checkpoint cost; it leaves the per-activity slope unchanged. SQLite commands are `4 × leaves +50` before and after.

At ten leaves, the66 PostgreSQL commands include11 from ScheduleActivity and11 inspection baseline reads, five from root-write leasing, four from command routing, three each from ownership acquisition, termination checks and ownership release. The generated summaries retain the complete phase/caller attribution, including the eight commands inside the checkpoint transaction. Some Npgsql BEGIN/reset messages share a request; the relay counts actual waiting round trips rather than treating each logical statement as one. Full wire-accounting assertions passed.

The measurement captures stacks and passes traffic through a relay, so its wall time is unsuitable for throughput attribution. Counts establish transport savings only. Existing historical multi-worker rates are not this baseline. This run used a shared Mac17,16 with15 logical/physical CPUs and64GiB RAM, macOS27.0.1, Docker29.8.2; observed load averages around43–53. No throughput/latency result is reported and no new cloud resources were provisioned. Independent-machine qualification remains with the existing Runtime Efficiency session.

## Causal and failure controls

- Original production code plus final SQLite controls:4 expected failures /4 passes; only initially-enabled cases fail because they observe automatic savepoints. The disabled cases preserve their prior behavior.
- Candidate:8/8 SQLite cases pass, covering initial settings true/false × success, marker failure after participant save, cancellation before marker save and lost commit acknowledgement. A subsequent caller-owned transaction on the same context proves the setting was restored and automatic savepoints work again when originally enabled.
- Actual PostgreSQL/SQLServer/MySQL checkpoint-store controls:12/12 pass, zero skips, required-provider mode. Each provider executes success, a real marker SQL failure after participant writes, cancellation and ambiguous commit acknowledgement. Fresh contexts verify participant+marker atomicity; same-writer replay/retry remains usable.
- Source reversal bypasses only the suppression assignment. Both-provider72-case measurement restores the old counts and the SQLite controls again fail4/8. Restoration passes the focused controls; the existing rollback/replay/fence-renewal/retained-root suites qualify unchanged shared guarantees.

The first focused build lacked the documented hydrated BPMN archive input; `python3 tools/bpmn/restore_evidence.py` restored and verified the pinned fixtures. The first test draft reused a fixture's fixed physical row ID for its later transaction; corrected before the meaningful4-red/4-green control. Those attempts are not counted as product failures or successful causal proof.

## Reproduction and evidence

Run `ELSA_COMMAND_COST_OUT=<empty-owned-directory> dotnet test Essentials/tests/Activities/Http/CommandCost/Tests/Elsa.Activities.Http.CommandCost.Tests.csproj -c Release` on the candidate and again with only `context.Database.AutoSavepointsEnabled = false` changed to its captured prior value. Never compare timing from this instrumented fixture.

[Comparison](evidence/comparison.json) contains all72 exact before/after totals. [Control summary](evidence/control-summary.md) and [candidate summary](evidence/candidate-summary.md) retain provider/mode/workload/phase/caller results. `control-runs.json.gz` and `candidate-runs.json.gz` hold every measured run with ordered commands, statement classifications, transactions, frames and wire counters; decompress with Python gzip+json or standard tools. SQL text is omitted by the existing report serializer. Focused TRX files retain all corrected red/green and provider outcomes.

Whole-suite, review, required-check and merge status are recorded on the implementation PR; a local proof alone does not establish delivery. Parent #2564 and program #2531 remain open for the other carry-overs and qualification.
