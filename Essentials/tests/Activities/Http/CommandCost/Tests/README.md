# EF command cost measurement

An opt-in tool that measures where every EF command of a workflow run comes from, on SQLite and on PostgreSQL. It
runs the same composition and reference workflows as the [EF command budgets](../../IntegrationTests/CommandBudgets/README.md)
and reproduces their counts exactly, but records each command instead of only counting it. Nothing is timed.

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

Each provider measures the three reference workflows under three persistence modes, and a Sequence of 5, 10, 20 and
40 WriteLine activities under two of them:

| Mode | Configuration |
|---|---|
| Durable | Today's `Immediate` mode: every checkpoint commits. |
| Balanced | Today's `Coalesced` mode with a segment cap no run reaches, which is what Balanced does with a per-commit activity cap above the run length. |
| Coalesced-50 | Today's `Coalesced` mode with its default cap of 50 checkpoints. |

For every command it records the statements in it (verb and table), whether it ran in a transaction, the engine phase
it ran in, taken from the runtime's own tracing spans (the work item being dispatched, the checkpoint being committed
and the persistence mode the policy chose), and the first Elsa frame past the persistence infrastructure, with its
file and line. On PostgreSQL every connection goes through a relay that reads the wire protocol and counts round trips,
statements and simple queries.

The run checks its own accounting: every statement on the PostgreSQL wire must be one the EF context sent or one of
EF's automatic savepoints, and every round trip one command, one COMMIT or one savepoint statement.

## Output

| File | Content |
|---|---|
| `summary.md` | Totals per run, the linear fit of every count over the WriteLine series, and every cost source ranked with the scaling class the fit shows. Regenerated from every run in the directory, so the second provider's test produces the combined report. |
| `commands/<provider>-<workflow>-<mode>-<activities>.tsv` | One line per command, with its frames and the start of its SQL. |
| `runs/*.json` | The measured runs the summary is built from. |

A cost source is per activity when it grows with the number of WriteLines in Balanced, where the commit count is fixed,
per commit when it grows only in Durable, where every activity commits, and per run when it grows in neither.

The Durable runs raise the drain orchestrator's cycle bound: with the default of 64, a Durable run of 20 or more
WriteLines stops with `DrainCycleLimitExceededException`. The bound only limits the loop and changes no count.
