# EF command cost measurement

An opt-in tool that measures where every EF command of a workflow run comes from, on SQLite and on PostgreSQL. It
runs the [EF command budgets](../../IntegrationTests/CommandBudgets/README.md)' own composition and runners, compiled in from
`ReferenceWorkflowRuns.cs`, and reproduces their counts exactly, but records each command instead of only counting it.
Nothing is timed.

## Running it

Every test skips unless `ELSA_COMMAND_COST_OUT` names an output directory, so neither the fast lane nor the nightly
container lane measures anything by default.

```bash
ELSA_COMMAND_COST_OUT=/tmp/command-cost \
  dotnet test Essentials/tests/Activities/Http/CommandCost/Tests/Elsa.Activities.Http.CommandCost.Tests.csproj
```

The PostgreSQL test starts a `postgres:16-alpine` container through Testcontainers, so it needs Docker. Set
`ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING` to an existing server to use that instead. Filter with
`--filter "FullyQualifiedName~SQLite"` or `~PostgreSQL` to measure one provider.

## What it measures

Each provider measures the three reference workflows, and a Sequence of 5, 10, 20 and 40 WriteLine activities, under
four configurations. Every one keeps the default `MaxActivitiesPerCommit` of 50, which no measured run reaches.

| Mode | Configuration |
|---|---|
| Durable | `PersistenceMode.Durable`: one commit per activity. |
| Balanced | `PersistenceMode.Balanced`: folds checkpoints and commits at suspension, completion, the end of a pass, before an activity that is not safe to repeat, and every 50 activities. |
| Ephemeral | `PersistenceMode.Ephemeral` with `RecordCompletion` on, the default. |
| Ephemeral-NoRecord | `PersistenceMode.Ephemeral` with `RecordCompletion` off: a run that was never committed leaves no record. |

For every command it records the statements in it (verb and table), whether it ran in a transaction, the engine phase
it ran in, taken from the runtime's own tracing spans (the work item being dispatched, the checkpoint being committed
and what the persistence policy decided for it), and the first Elsa frame past the persistence infrastructure, with its
file and line. On PostgreSQL every connection goes through a relay that reads the wire protocol and counts round trips,
statements and simple queries.

### How a command's caller is found

A command's caller is the first Elsa frame past the persistence infrastructure (stores, decorators, coalescing
overlays, paging extensions). The `attribution` column of the command files and the summary's attribution table say
how it was found:

| Attribution | Meaning |
|---|---|
| `stack` | The stack the command was issued on reaches the caller. Every SQLite command is attributed this way, because SQLite's asynchronous calls complete synchronously and never lose a stack. |
| `save-changes` | The command ran inside `SaveChanges` after an asynchronous hop, so no Elsa frame was left on its stack. It is attributed to the `SaveChanges` call site, captured when `SaveChanges` was entered. On PostgreSQL this covers the batched writes and commit-marker inserts after the savepoint round trip: a batch carries the rows of several participants and has no single Elsa caller, so the call site that saved them owns it. |
| `stitched` | A store method resumed after real I/O, so its stack holds the store but not the store's caller. The stack is completed with the latest whole stack of the same asynchronous flow that ran through the same store frame, normally the store's own earlier command or the opening of its connection. `<~` in the frames marks the seam. |
| `none` | No caller was found. The run fails if any command ends up here. |

Each measured call, each request of the measured host and each engine span starts a flow's memory of whole stacks. On
the runs measured in October 2026, every PostgreSQL statement was attributed to the same caller as the matching SQLite
statement.

The run checks its own accounting: every statement on the PostgreSQL wire must be one the EF context sent or one of
EF's automatic savepoints, and every round trip one command, one COMMIT or one savepoint statement.

## Output

| File | Content |
|---|---|
| `summary.md` | Totals per run, the linear fit of every count over the WriteLine series, and every cost source ranked with the scaling class the fit shows. Regenerated from every run in the directory, so the second provider's test produces the combined report. |
| `commands/<provider>-<workflow>-<mode>-<activities>.tsv` | One line per command, with its frames and the start of its SQL. |
| `runs/*.json` | The measured runs the summary is built from. A provider's measurement first deletes that provider's earlier runs and command files, so the summary never mixes in a stale run. |

A cost source is per activity when it grows with the number of WriteLines in Balanced, where the commit count is fixed,
per commit when it grows only in Durable, where every activity commits, and per run when it grows in neither. The
series is fitted in every mode; the cost sources table compares Balanced with Durable.
