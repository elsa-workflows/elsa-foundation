using Elsa.Activities.Http.IntegrationTests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// A persistence mode as the measurement names it: the host's mode and its RecordCompletion setting, which only
/// Ephemeral reads. Balanced folds up to <see cref="ReferenceWorkflowRuns.MaxActivitiesPerCommit"/> activities into one
/// commit, the production default, which no measured run reaches.
/// </summary>
internal sealed record CommandCostMode(string Name, PersistenceMode Mode, bool RecordCompletion = true)
{
    public static readonly CommandCostMode Durable = new("Durable", PersistenceMode.Durable);
    public static readonly CommandCostMode Balanced = new("Balanced", PersistenceMode.Balanced);
    public static readonly CommandCostMode Ephemeral = new("Ephemeral", PersistenceMode.Ephemeral);
    public static readonly CommandCostMode EphemeralNoRecord = new("Ephemeral-NoRecord", PersistenceMode.Ephemeral, RecordCompletion: false);

    public static readonly IReadOnlyList<CommandCostMode> All = [Durable, Balanced, Ephemeral, EphemeralNoRecord];
}

/// <summary>One measured run: what it cost, command by command, and in total.</summary>
internal sealed record CommandCostRun(
    string Provider,
    string Workflow,
    string Mode,
    int Activities,
    IReadOnlyList<CommandCostEntry> Commands,
    CommandCostCounters Counters,
    PostgresWireCounts? Wire)
{
    public int Statements => Commands.Sum(command => command.Statements.Count);

    /// <summary>Durable store commits: each one writes exactly one commit marker row.</summary>
    public int CommitMarkers => Commands.Sum(command => command.Statements.Count(statement => statement is { Kind: "INSERT", Table: "checkpoint_commit" }));
}

/// <summary>A database a measured host runs on: a SQLite file per run, or a PostgreSQL database per run behind the wire counter.</summary>
internal interface ICommandCostDatabase
{
    string Provider { get; }

    PostgresWireCounter? Wire { get; }

    Task<HttpEndpointHostFixture> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices);

    void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder);
}

internal sealed class SqliteCommandCostDatabase : ICommandCostDatabase
{
    public string Provider => "Sqlite";

    public PostgresWireCounter? Wire => null;

    public Task<HttpEndpointHostFixture> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices) =>
        HttpEndpointHostFixture.StartDurableSqliteAsync(mode.Mode, ReferenceWorkflowRuns.MaxActivitiesPerCommit, configureServices, mode.RecordCompletion);

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(recorder));
}

internal sealed class PostgreSqlCommandCostDatabase(CommandCostPostgreSqlServer server) : ICommandCostDatabase
{
    public string Provider => "PostgreSql";

    public PostgresWireCounter? Wire => server.Wire;

    public async Task<HttpEndpointHostFixture> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices) =>
        await HttpEndpointHostFixture.StartDurableAsync(
            "PostgreSql",
            await server.CreateDatabaseAsync(),
            mode.Mode,
            ReferenceWorkflowRuns.MaxActivitiesPerCommit,
            configureServices,
            mode.RecordCompletion);

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimePostgreSqlDbContext>(builder => builder.AddInterceptors(recorder));
}

/// <summary>
/// The reference workflows of the EF command budgets, run by the budgets' own runners, with the WriteLine sequence at any
/// length to fit the cost per activity. Each scenario counts exactly what the budgets count: the run, not host start,
/// schema installation, publication or the assertions on the outcome.
/// </summary>
internal static class CommandCostScenarios
{
    public static async Task<CommandCostRun> MeasureAsync(ICommandCostDatabase database, string workflow, CommandCostMode mode, int writeLines = 10)
    {
        using var recorder = new CommandCostRecorder();
        await using var fixture = await database.StartHostAsync(mode, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            database.AddInterceptor(services, recorder);
            services.Replace(ServiceDescriptor.Singleton(recorder.Tracer));
        });

        var window = new MeasurementWindow(recorder, database.Wire);
        await ReferenceWorkflowRuns.RunAsync(workflow, fixture, window.MeasureAsync, writeLines, mode.RecordCompletion);

        return new CommandCostRun(database.Provider, workflow, mode.Name, Activities(workflow, writeLines), window.Commands, window.Counters, window.Wire);
    }

    /// <summary>The activity executions of a run: the Sequence and its WriteLines, the four HTTP activities, or the Parallel and its two branches.</summary>
    private static int Activities(string workflow, int writeLines) => workflow switch
    {
        ReferenceWorkflowRuns.WriteLineSequence => writeLines + 1,
        ReferenceWorkflowRuns.HttpReference => 4,
        ReferenceWorkflowRuns.ForkJoinResume => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "Unknown reference workflow.")
    };

    /// <summary>Accumulates what one or more measured spans of a run cost; the gaps between spans are not counted.</summary>
    private sealed class MeasurementWindow(CommandCostRecorder recorder, PostgresWireCounter? wire)
    {
        private readonly List<CommandCostEntry> _commands = [];

        public IReadOnlyList<CommandCostEntry> Commands => _commands;
        public CommandCostCounters Counters { get; private set; } = CommandCostCounters.Zero;
        public PostgresWireCounts? Wire { get; private set; } = wire is null ? null : PostgresWireCounts.Zero;

        public async Task MeasureAsync(Func<Task> run)
        {
            var countersBefore = recorder.Counters;
            var wireBefore = wire?.Counts;
            await run();
            var countersAfter = recorder.Counters;

            _commands.AddRange(recorder.Commands
                .Where(command => command.Ordinal > countersBefore.Commands && command.Ordinal <= countersAfter.Commands)
                .OrderBy(command => command.Ordinal));
            Counters = Counters.Plus(countersAfter).Plus(countersBefore, sign: -1);
            if (wire is not null)
                Wire = Wire!.Plus(wire.Counts).Plus(wireBefore!, sign: -1);
        }
    }
}
