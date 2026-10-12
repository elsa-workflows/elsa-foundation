using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Http.IntegrationTests;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.CommandCost;

/// <summary>
/// A persistence mode as the measurement names it. Durable is today's Immediate mode. Balanced is today's Coalesced
/// mode with a segment cap no run reaches, which is what Balanced with a per-commit activity cap above the run length
/// does. Coalesced-50 is today's Coalesced default, whose cap counts checkpoints rather than activities.
/// </summary>
internal sealed record CommandCostMode(string Name, CheckpointPersistenceMode Mode, int MaxSegmentCheckpoints)
{
    public static readonly CommandCostMode Durable = new("Durable", CheckpointPersistenceMode.Immediate, 50);
    public static readonly CommandCostMode Balanced = new("Balanced", CheckpointPersistenceMode.Coalesced, 100_000);
    public static readonly CommandCostMode Coalesced50 = new("Coalesced-50", CheckpointPersistenceMode.Coalesced, 50);
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

    /// <summary>Durable checkpoint commits: each one writes exactly one commit marker row.</summary>
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
        HttpEndpointHostFixture.StartDurableSqliteAsync(mode.Mode, mode.MaxSegmentCheckpoints, configureServices);

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(recorder));
}

internal sealed class PostgreSqlCommandCostDatabase(CommandCostPostgreSqlServer server) : ICommandCostDatabase
{
    public string Provider => "PostgreSql";

    public PostgresWireCounter? Wire => server.Wire;

    public async Task<HttpEndpointHostFixture> StartHostAsync(CommandCostMode mode, Action<IServiceCollection> configureServices) =>
        await HttpEndpointHostFixture.StartDurableAsync("PostgreSql", await server.CreateDatabaseAsync(), mode.Mode, mode.MaxSegmentCheckpoints, configureServices);

    public void AddInterceptor(IServiceCollection services, CommandCostRecorder recorder) =>
        services.ConfigureDbContext<RuntimePostgreSqlDbContext>(builder => builder.AddInterceptors(recorder));
}

/// <summary>
/// The reference workflows of the EF command budgets, plus a WriteLine sequence of any length to fit the cost per
/// activity. Each scenario counts exactly what the budgets count: the run, not host start, schema installation,
/// publication or the assertions on the outcome.
/// </summary>
internal static class CommandCostScenarios
{
    public const string WriteLineSequence = "writeline-sequence";
    public const string HttpReference = "http-reference";
    public const string ForkJoinResume = "fork-join-resume";

    /// <summary>
    /// What runs on a timer instead of because of the workflow, as in the budgets: lease and claim renewals and the EF
    /// schema gate's checks. A period no run reaches keeps the count a property of the workflow alone.
    /// </summary>
    private static readonly TimeSpan BeyondAnyRun = TimeSpan.FromHours(1);

    public static async Task<CommandCostRun> MeasureAsync(ICommandCostDatabase database, string workflow, CommandCostMode mode, int writeLines = 10)
    {
        using var recorder = new CommandCostRecorder();
        await using var fixture = await database.StartHostAsync(mode, services =>
        {
            new ActivitiesControlFlowFeature().ConfigureServices(services);
            new JavaScriptFeature().ConfigureServices(services);
            new JintFeature().ConfigureServices(services);
            database.AddInterceptor(services, recorder);
            services.Replace(ServiceDescriptor.Singleton(recorder.Tracer));
            // The default bound of 64 drain cycles stops a Durable run of 20 or more WriteLines with
            // DrainCycleLimitExceededException. The bound only limits the loop; it changes no count.
            services.Replace(ServiceDescriptor.Singleton(new WorkflowDrainOrchestratorOptions(maxDrainCycles: 100_000)));
            services.Replace(ServiceDescriptor.Singleton(new RuntimeExecutionOwnershipOptions { LeaseDuration = BeyondAnyRun }));
            services.Replace(ServiceDescriptor.Singleton(new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = BeyondAnyRun }));
            services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.RootWriteLeaseDuration = BeyondAnyRun);
            services.Configure<EfSchemaFinalizationOptions>(options =>
            {
                options.RefreshInterval = BeyondAnyRun;
                options.EvaluationInterval = BeyondAnyRun;
            });
            services.Configure<EfSchemaBackfillOptions>(options => options.CheckInterval = BeyondAnyRun);
        });

        var window = new MeasurementWindow(recorder, database.Wire);
        var activities = workflow switch
        {
            WriteLineSequence => await RunWriteLineSequenceAsync(fixture, window, writeLines),
            HttpReference => await RunHttpReferenceAsync(fixture, window),
            ForkJoinResume => await RunForkJoinResumeAsync(fixture, window),
            _ => throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "Unknown reference workflow.")
        };
        await AssertSettledAsync(fixture);

        return new CommandCostRun(database.Provider, workflow, mode.Name, activities, window.Commands, window.Counters, window.Wire);
    }

    /// <summary>A Sequence of <paramref name="writeLines"/> WriteLine activities: <paramref name="writeLines"/> + 1 activity executions.</summary>
    private static async Task<int> RunWriteLineSequenceAsync(HttpEndpointHostFixture fixture, MeasurementWindow window, int writeLines)
    {
        var executable = ReferenceWorkflows.Executable(
            $"writeline-sequence-{writeLines}",
            ReferenceWorkflows.Sequence("sequence", Enumerable.Range(1, writeLines)
                .Select(line => ReferenceWorkflows.Clr($"write-{line}", typeof(WriteLine), [("text", $"line {line}")]))
                .ToArray()));
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        await window.MeasureAsync(async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        return writeLines + 1;
    }

    private static async Task<int> RunHttpReferenceAsync(HttpEndpointHostFixture fixture, MeasurementWindow window)
    {
        await fixture.PublishAsync(ReferenceWorkflows.HttpReference());

        HttpResponseMessage? response = null;
        await window.MeasureAsync(async () =>
        {
            using var body = new StringContent("""{"firstName":"Alice","lastName":"Smith"}""", Encoding.UTF8, "application/json");
            response = await fixture.Client.PostAsync($"/workflows/http/{ReferenceWorkflows.HttpPath}", body);
        });

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
            Assert.Equal("Alice Smith", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.SingleWorkflowExecutionAsync()).Status);
        return 4;
    }

    private static async Task<int> RunForkJoinResumeAsync(HttpEndpointHostFixture fixture, MeasurementWindow window)
    {
        var executable = ReferenceWorkflows.ForkJoinResume();
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        await window.MeasureAsync(async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        var bookmark = Assert.Single(await fixture.ListBookmarksAsync(executionId));
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowExecutionAsync(executionId)).Status);

        BookmarkResumeDispatchResult? resumed = null;
        await window.MeasureAsync(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    workflowExecutionId: executionId,
                    stimulusType: bookmark.StimulusType,
                    stimulusHash: bookmark.StimulusHash,
                    input: JsonSerializer.SerializeToElement(new EventReceived(ReferenceWorkflows.EventName))));
        });

        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed!.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        return 3;
    }

    /// <summary>The run left nothing for a background pump to do after the count was taken, which the count would miss.</summary>
    private static async Task AssertSettledAsync(HttpEndpointHostFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        Assert.All(
            await context.RuntimePostCommitOutbox.AsNoTracking().Select(item => item.Status).ToArrayAsync(),
            status => Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (RuntimePostCommitOutboxStatus)status));
        Assert.Empty(await context.IncidentStates.AsNoTracking().ToArrayAsync());
    }

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
