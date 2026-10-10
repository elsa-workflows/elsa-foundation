// EF command budgets: how many EF commands each reference workflow may cost, per checkpoint persistence mode, on
// SQLite. The count is deterministic and is a correctness gate, not a performance measurement; nothing here is timed.
//
// The budgets live in ef-command-budgets.json next to this file. Lowering a budget is a normal change. Raising one
// needs a stated reason in the pull request that raises it.
using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Runs each reference workflow on a fresh SQLite database under each checkpoint persistence mode and fails when the
/// EF commands it costs exceed the committed budget. Only the run itself is counted: host start, schema installation
/// and publication come before the count, and the assertions that check the outcome come after it.
/// </summary>
public sealed class EfCommandBudgetTests(ITestOutputHelper output)
{
    private const string BudgetFile = "ef-command-budgets.json";
    private const string BasePath = "/workflows/http/";
    private const int MaxSegmentCheckpoints = 50;

    /// <summary>
    /// What runs on a timer instead of because of the workflow: the execution lease, the scheduler work claim and the
    /// executable root-write lease renew every third of their duration (20 seconds by default), and the EF schema gate
    /// refreshes, evaluates and checks for backfill work every 15 to 30 seconds from host start. A run slowed past those
    /// by a busy machine counted that work too, so the count depended on wall time. A period no run reaches keeps the
    /// count a property of the workflow alone.
    /// </summary>
    private static readonly TimeSpan BeyondAnyRun = TimeSpan.FromHours(1);

    private static readonly IReadOnlyDictionary<string, Func<HttpEndpointHostFixture, CommandCaptureInterceptor, Task<int>>> Workflows =
        new Dictionary<string, Func<HttpEndpointHostFixture, CommandCaptureInterceptor, Task<int>>>(StringComparer.Ordinal)
        {
            ["writeline-sequence"] = RunWriteLineSequenceAsync,
            ["http-reference"] = RunHttpReferenceAsync,
            ["fork-join-resume"] = RunForkJoinResumeAsync
        };

    public static TheoryData<string, CheckpointPersistenceMode> Cases()
    {
        var cases = new TheoryData<string, CheckpointPersistenceMode>();
        foreach (var workflow in Workflows.Keys)
        foreach (var mode in Enum.GetValues<CheckpointPersistenceMode>())
            cases.Add(workflow, mode);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Reference_workflow_stays_within_its_EF_command_budget(string workflow, CheckpointPersistenceMode mode)
    {
        var actual = await MeasureAsync(workflow, mode);

        int? budget = ReadBudgets().TryGetValue(workflow, out var budgets) && budgets.TryGetValue(mode.ToString(), out var committed)
            ? committed
            : null;
        output.WriteLine($"EF commands: workflow '{workflow}', mode '{mode}': actual {actual}, budget {budget?.ToString() ?? "none"}.");
        Assert.True(budget is not null,
            $"No EF command budget for workflow '{workflow}' under mode '{mode}' (actual {actual}). Add it to {BudgetFile}.");
        Assert.True(actual <= budget,
            $"EF command budget exceeded for workflow '{workflow}' under mode '{mode}': actual {actual}, budget {budget}. " +
            $"Raising a budget in {BudgetFile} needs a stated reason in the pull request.");
    }

    [Fact]
    public void Every_budget_names_a_reference_workflow_and_a_persistence_mode()
    {
        var modes = Enum.GetNames<CheckpointPersistenceMode>();
        foreach (var (workflow, budgets) in ReadBudgets())
        {
            Assert.True(Workflows.ContainsKey(workflow), $"{BudgetFile} budgets unknown workflow '{workflow}'.");
            foreach (var mode in budgets.Keys)
                Assert.True(modes.Contains(mode), $"{BudgetFile} budgets workflow '{workflow}' under unknown mode '{mode}'.");
        }
    }

    private static async Task<int> MeasureAsync(string workflow, CheckpointPersistenceMode mode)
    {
        var capture = new CommandCaptureInterceptor();
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, MaxSegmentCheckpoints, services =>
        {
            new ActivitiesControlFlowFeature().ConfigureServices(services);
            new JavaScriptFeature().ConfigureServices(services);
            new JintFeature().ConfigureServices(services);
            services.ConfigureDbContext<RuntimeSqliteDbContext>(builder => builder.AddInterceptors(capture));
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

        var actual = await Workflows[workflow](fixture, capture);
        await AssertSettledAsync(fixture);
        return actual;
    }

    private static async Task<int> RunWriteLineSequenceAsync(HttpEndpointHostFixture fixture, CommandCaptureInterceptor capture)
    {
        var executable = ReferenceWorkflows.WriteLineSequence();
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        var commands = await CountAsync(capture, async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        return commands;
    }

    private static async Task<int> RunHttpReferenceAsync(HttpEndpointHostFixture fixture, CommandCaptureInterceptor capture)
    {
        await fixture.PublishAsync(ReferenceWorkflows.HttpReference());

        HttpResponseMessage? response = null;
        var commands = await CountAsync(capture, async () =>
        {
            using var body = new StringContent("""{"firstName":"Alice","lastName":"Smith"}""", Encoding.UTF8, "application/json");
            response = await fixture.Client.PostAsync($"{BasePath}{ReferenceWorkflows.HttpPath}", body);
        });

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
            Assert.Equal("Alice Smith", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.SingleWorkflowExecutionAsync()).Status);
        return commands;
    }

    private static async Task<int> RunForkJoinResumeAsync(HttpEndpointHostFixture fixture, CommandCaptureInterceptor capture)
    {
        var executable = ReferenceWorkflows.ForkJoinResume();
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        var commands = await CountAsync(capture, async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        var bookmark = Assert.Single(await fixture.ListBookmarksAsync(executionId));
        Assert.Equal(ReferenceWorkflows.EventNodeId, bookmark.ExecutableNodeId);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowExecutionAsync(executionId)).Status);

        BookmarkResumeDispatchResult? resumed = null;
        commands += await CountAsync(capture, async () =>
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
        Assert.Empty(await fixture.ListBookmarksAsync(executionId));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        return commands;
    }

    private static async Task<int> CountAsync(CommandCaptureInterceptor capture, Func<Task> run)
    {
        var before = capture.Commands.Count;
        await run();
        return capture.Commands.Count - before;
    }

    /// <summary>
    /// Checks the run left nothing for a background pump to do after the count was taken, which that count would miss: no
    /// queued scheduler work, no post-commit work short of delivered, and no incident.
    /// </summary>
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

    private static Dictionary<string, Dictionary<string, int>> ReadBudgets() =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
            File.ReadAllText(Path.Join(AppContext.BaseDirectory, "CommandBudgets", BudgetFile)))
        ?? throw new InvalidDataException($"{BudgetFile} is empty.");
}
