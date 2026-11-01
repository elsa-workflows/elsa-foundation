using Elsa.Activities.Http.IntegrationTests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// A persistence mode as the measurement names it: the host's mode and its RecordCompletion setting, which only
/// Ephemeral reads. Balanced folds up to <see cref="ReferenceWorkflowRuns.MaxActivitiesPerCommit"/> activities into one
/// commit, the production default; the long suspension reference crosses it.
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
    CommandCostCounts Counts,
    PostgreSqlWireCounts? Wire)
{
    public int Statements => Commands.Sum(command => command.Statements.Count);

    /// <summary>Durable store commits: each one writes exactly one commit marker row.</summary>
    public int CommitMarkers => Commands.Sum(command => command.Statements.Count(statement => statement is { Kind: "INSERT", Table: "checkpoint_commit" }));
}

/// <summary>A database a measured host runs on: a SQLite file per run, or a PostgreSQL database per run behind the wire counter.</summary>
internal interface IMeasuredDatabase
{
    string Provider { get; }

    PostgreSqlWireCounter? Wire { get; }

    Task<MeasuredHost> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices);

    void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder);
}

internal sealed class SqliteDatabase : IMeasuredDatabase
{
    public string Provider => "Sqlite";

    public PostgreSqlWireCounter? Wire => null;

    public async Task<MeasuredHost> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices) =>
        new(await HttpEndpointHostFixture.StartDurableSqliteAsync(mode.Mode, ReferenceWorkflowRuns.MaxActivitiesPerCommit, configureServices, mode.RecordCompletion));

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(recorder));
}

internal sealed class PostgreSqlDatabase(CommandCostPostgreSqlServer server) : IMeasuredDatabase
{
    public string Provider => "PostgreSql";

    public PostgreSqlWireCounter? Wire => server.Wire;

    public async Task<MeasuredHost> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices)
    {
        var database = await server.CreateDatabaseAsync();
        try
        {
            return new MeasuredHost(
                await HttpEndpointHostFixture.StartDurableAsync(
                    "PostgreSql",
                    database.ConnectionString,
                    mode.Mode,
                    ReferenceWorkflowRuns.MaxActivitiesPerCommit,
                    configureServices,
                    mode.RecordCompletion),
                database);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimePostgreSqlDbContext>(builder => builder.AddInterceptors(recorder));
}

/// <summary>A measured host; disposing it stops the host and then tears down its database, if that needs anything.</summary>
internal sealed class MeasuredHost(HttpEndpointHostFixture fixture, IAsyncDisposable? database = null) : IAsyncDisposable
{
    public HttpEndpointHostFixture Fixture => fixture;

    public async ValueTask DisposeAsync()
    {
        await fixture.DisposeAsync();
        if (database is not null)
            await database.DisposeAsync();
    }
}

/// <summary>
/// The reference workflows of the EF command budgets, run by the budgets' own runners, with the WriteLine sequence at any
/// length to fit the cost per activity. Each scenario counts exactly what the budgets count: the run, not host start,
/// schema installation, publication or the assertions on the outcome.
/// </summary>
internal static class CommandCostScenarios
{
    /// <summary>
    /// Not a workflow: one transaction that sets a savepoint, runs a statement, rolls back to the savepoint and rolls
    /// back. No reference workflow rolls anything back, so this checks the wire accounting for rollbacks.
    /// </summary>
    public const string RollbackProbe = "rollback-probe";

    public static async Task<CommandCostRun> MeasureAsync(IMeasuredDatabase database, string workflow, CommandCostMode mode, int writeLines = 10)
    {
        using var recorder = new CommandCostRecorder();
        await using var host = await database.StartHostAsync(mode, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            database.AddInterceptor(services, recorder);
            services.Replace(ServiceDescriptor.Singleton(recorder.Tracer));
            services.AddSingleton<IStartupFilter>(new FlowPerRequest(recorder));
        });

        var fixture = host.Fixture;
        var window = new MeasurementWindow(recorder, database.Wire);
        if (workflow == RollbackProbe)
            await window.MeasureAsync(() => RunRollbackProbeAsync(fixture));
        else
            await ReferenceWorkflowRuns.RunAsync(workflow, fixture, window.MeasureAsync, writeLines, mode.RecordCompletion);

        return new CommandCostRun(database.Provider, workflow, mode.Name, Activities(workflow, writeLines), window.Commands, window.Counts, window.Wire);
    }

    /// <summary>Includes structural nodes as well as the leaf activity executions in each reference run.</summary>
    private static int Activities(string workflow, int writeLines) => workflow switch
    {
        ReferenceWorkflowRuns.WriteLineSequence => writeLines + 1,
        ReferenceWorkflowRuns.HttpReference => 4,
        ReferenceWorkflowRuns.ForkJoinResume => 3,
        ReferenceWorkflowRuns.SuspendingSequence100 => 103,
        RollbackProbe => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "Unknown reference workflow.")
    };

    private static async Task RunRollbackProbeAsync(HttpEndpointHostFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await transaction.CreateSavepointAsync("probe");
        await context.Database.ExecuteSqlRawAsync("SELECT 1");
        await transaction.RollbackToSavepointAsync("probe");
        await transaction.RollbackAsync();
    }

    /// <summary>
    /// Gives each request of the measured host a stack memory before any middleware runs. TestServer runs a request
    /// without the caller's execution context, so the memory the measurement window began does not reach it.
    /// </summary>
    private sealed class FlowPerRequest(CommandCostRecorder recorder) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                recorder.BeginFlow();
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>Accumulates what one or more measured spans of a run cost; the gaps between spans are not counted.</summary>
    private sealed class MeasurementWindow(CommandCostRecorder recorder, PostgreSqlWireCounter? wire)
    {
        private readonly List<CommandCostEntry> _commands = [];

        public IReadOnlyList<CommandCostEntry> Commands => _commands;
        public CommandCostCounts Counts { get; private set; } = CommandCostCounts.Zero;
        public PostgreSqlWireCounts? Wire { get; private set; } = wire is null ? null : PostgreSqlWireCounts.Zero;

        public async Task MeasureAsync(Func<Task> run)
        {
            var countsBefore = recorder.Counts;
            var wireBefore = wire?.Counts;
            recorder.BeginFlow();
            await run();
            var countsAfter = recorder.Counts;

            _commands.AddRange(recorder.Commands
                .Where(command => command.Ordinal > countsBefore.Commands && command.Ordinal <= countsAfter.Commands)
                .OrderBy(command => command.Ordinal));
            Counts = Counts.Plus(countsAfter).Plus(countsBefore, sign: -1);
            if (wire is not null)
                Wire = Wire!.Plus(wire.Counts).Plus(wireBefore!, sign: -1);
        }
    }
}
