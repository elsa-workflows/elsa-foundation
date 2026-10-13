// Measures where each EF command of a workflow run comes from. Opt-in: every test skips unless ELSA_COMMAND_COST_OUT
// names a directory, where the measured runs, one TSV of commands per run and summary.md are written. See README.md.
using Elsa.Activities.Http.IntegrationTests;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Elsa.Activities.Http.CommandCost;

/// <summary>Whether measurement is switched on, and where its output goes.</summary>
internal static class CommandCostMeasurement
{
    public const string OutputVariable = "ELSA_COMMAND_COST_OUT";

    public static string? OutputDirectory =>
        Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } directory ? Path.GetFullPath(directory) : null;
}

[CollectionDefinition(Name)]
public sealed class CommandCostCollection : ICollectionFixture<CommandCostPostgreSqlServer>
{
    public const string Name = "command-cost";
}

[Collection(CommandCostCollection.Name)]
[Trait("Category", "Measurement")]
public sealed class CommandCostMeasurementTests(CommandCostPostgreSqlServer postgreSql)
{
    [SkippableFact]
    public Task Measures_the_reference_workflows_and_the_WriteLine_series_on_SQLite() =>
        MeasureAsync(new SqliteDatabase());

    [SkippableFact]
    public Task Measures_the_reference_workflows_and_the_WriteLine_series_on_PostgreSQL() =>
        MeasureAsync(new PostgreSqlDatabase(postgreSql));

    private static async Task MeasureAsync(IMeasuredDatabase database)
    {
        var output = CommandCostMeasurement.OutputDirectory;
        Skip.If(output is null, $"Set {CommandCostMeasurement.OutputVariable} to an output directory to measure EF command cost.");

        CommandCostReport.ClearRuns(output!, database.Provider);
        var runs = new List<CommandCostRun>();
        foreach (var mode in CommandCostMode.All)
        {
            foreach (var workflow in ReferenceWorkflowRuns.Workflows)
                runs.Add(await CommandCostScenarios.MeasureAsync(database, workflow, mode));
            foreach (var writeLines in CommandCostReport.WriteLineSeries.Where(count => count != 10))
                runs.Add(await CommandCostScenarios.MeasureAsync(database, ReferenceWorkflowRuns.WriteLineSequence, mode, writeLines));
        }

        foreach (var run in runs)
            await CommandCostReport.WriteRunAsync(output!, run);
        await File.WriteAllTextAsync(Path.Join(output, "summary.md"), CommandCostReport.Render(CommandCostReport.ReadRuns(output!)));

        // Every command names the Elsa caller that issued it; a command without one would fall into a cost source no code owns.
        Assert.All(runs, run => Assert.True(run.Commands.All(command => command.Caller != "?"),
            $"{run.Provider} {run.Workflow} {run.Mode} {run.Activities}: {run.Commands.Count(command => command.Caller == "?")} commands have no Elsa caller."));

        // Every durable commit writes one commit marker inside a transaction of its own, so the transactions that wrote a
        // marker are as many as the markers. Fewer means two real transactions were given one id.
        Assert.All(runs, run => Assert.True(CommandCostReport.CommitTransactions(run).Count == run.CommitMarkers,
            $"{run.Provider} {run.Workflow} {run.Mode} {run.Activities}: {run.CommitMarkers} commit markers were written in " +
            $"{CommandCostReport.CommitTransactions(run).Count} distinct transaction ids."));

        // Every statement on the wire is one the EF context sent, or a savepoint EF set or released around a save inside
        // an open transaction, which Npgsql runs without passing through the command interceptor. Every round trip is one
        // command, one COMMIT or one of those savepoint statements; Npgsql writes BEGIN and the reset of a pooled
        // connection together with the next command. Any other difference means traffic the attribution misses, and
        // the report's counts would not add up.
        Assert.All(runs.Where(run => run.Wire is not null), run =>
        {
            var savepointStatements = run.Counts.SavepointsCreated + run.Counts.SavepointsReleased;
            Assert.True(run.Wire!.Executes == run.Statements + savepointStatements,
                $"{run.Provider} {run.Workflow} {run.Mode} {run.Activities}: the wire carried {run.Wire.Executes} statements; the EF context sent " +
                $"{run.Statements} and {savepointStatements} savepoint statements.");
            Assert.True(run.Wire.RoundTrips == run.Commands.Count + run.Counts.TransactionsCommitted + savepointStatements,
                $"{run.Provider} {run.Workflow} {run.Mode} {run.Activities}: {run.Wire.RoundTrips} round trips; the EF context sent " +
                $"{run.Commands.Count} commands, {run.Counts.TransactionsCommitted} commits and {savepointStatements} savepoint statements.");
        });
    }
}
