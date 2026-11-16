using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// How the EF command budgets run their reference workflows: the host composition, one runner per workflow and the
/// check that a run left nothing behind. The command-cost measurement in <c>Essentials/tests/Activities/Http/CommandCost</c>
/// compiles this file in, so both count the same runs. A runner hands each span of the run that counts to the caller's
/// <c>measure</c>; host start, publication and the assertions on the outcome stay outside those spans.
/// </summary>
internal static class ReferenceWorkflowRuns
{
    public const string WriteLineSequence = "writeline-sequence";
    public const string HttpReference = "http-reference";
    public const string ForkJoinResume = "fork-join-resume";
    public const string SuspendingSequence100 = "suspending-sequence-100";

    /// <summary>Balanced's production cap on activities folded into one commit; the long suspension reference crosses it.</summary>
    public const int MaxActivitiesPerCommit = 50;

    public static readonly IReadOnlyList<string> Workflows = [WriteLineSequence, HttpReference, ForkJoinResume, SuspendingSequence100];

    /// <summary>
    /// What runs on a timer instead of because of the workflow: the execution lease, the scheduler work claim and the
    /// executable root-write lease renew every third of their duration (20 seconds by default), and the EF schema gate
    /// refreshes, evaluates and checks for backfill work every 15 to 30 seconds from host start. A run slowed past those
    /// by a busy machine counted that work too, so the count depended on wall time. A period no run reaches keeps the
    /// count a property of the workflow alone.
    /// </summary>
    private static readonly TimeSpan BeyondAnyRun = TimeSpan.FromHours(1);

    /// <summary>The activities the reference workflows need, and every timer period set beyond any run.</summary>
    public static void ComposeHost(IServiceCollection services)
    {
        new ActivitiesControlFlowFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton(new RuntimeExecutionOwnershipOptions { LeaseDuration = BeyondAnyRun }));
        services.Replace(ServiceDescriptor.Singleton(new RuntimeSchedulerWorkClaimOptions { VisibilityTimeout = BeyondAnyRun }));
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.RootWriteLeaseDuration = BeyondAnyRun);
        services.Configure<EfSchemaFinalizationOptions>(options =>
        {
            options.RefreshInterval = BeyondAnyRun;
            options.EvaluationInterval = BeyondAnyRun;
        });
        services.Configure<EfSchemaBackfillOptions>(options => options.CheckInterval = BeyondAnyRun);
    }

    /// <summary>
    /// Runs <paramref name="workflow"/> on <paramref name="fixture"/>, checks its outcome, and checks it left nothing for
    /// a background pump to do. <paramref name="writeLines"/> sets the length of the WriteLine sequence.
    /// <paramref name="recordsCompletion"/> is false when the host runs Ephemeral with RecordCompletion off, where a run
    /// that was never committed leaves no record to check.
    /// </summary>
    public static async Task RunAsync(
        string workflow,
        HttpEndpointHostFixture fixture,
        Func<Func<Task>, Task> measure,
        int writeLines = 10,
        bool recordsCompletion = true)
    {
        switch (workflow)
        {
            case WriteLineSequence:
                await RunWriteLineSequenceAsync(fixture, measure, writeLines, recordsCompletion);
                break;
            case HttpReference:
                await RunHttpReferenceAsync(fixture, measure, recordsCompletion);
                break;
            case ForkJoinResume:
                await RunForkJoinResumeAsync(fixture, measure);
                break;
            case SuspendingSequence100:
                await RunForkJoinResumeAsync(fixture, measure, longSequence: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "Unknown reference workflow.");
        }

        await AssertSettledAsync(fixture);
    }

    private static async Task RunWriteLineSequenceAsync(HttpEndpointHostFixture fixture, Func<Func<Task>, Task> measure, int writeLines, bool recordsCompletion)
    {
        var executable = ReferenceWorkflows.WriteLineSequence(WriteLineSequence, writeLines);
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        await measure(async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        await AssertCompletedAsync(fixture, recordsCompletion, executionId);
    }

    private static async Task RunHttpReferenceAsync(HttpEndpointHostFixture fixture, Func<Func<Task>, Task> measure, bool recordsCompletion)
    {
        await fixture.PublishAsync(ReferenceWorkflows.HttpReference());

        HttpResponseMessage? response = null;
        await measure(async () =>
        {
            using var body = new StringContent("""{"firstName":"Alice","lastName":"Smith"}""", Encoding.UTF8, "application/json");
            response = await fixture.Client.PostAsync($"/workflows/http/{ReferenceWorkflows.HttpPath}", body);
        });

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
            Assert.Equal("Alice Smith", await response.Content.ReadAsStringAsync());
        }
        await AssertCompletedAsync(fixture, recordsCompletion);
    }

    private static async Task RunForkJoinResumeAsync(HttpEndpointHostFixture fixture, Func<Func<Task>, Task> measure, bool longSequence = false)
    {
        var executable = longSequence ? ReferenceWorkflows.SuspendingSequence100() : ReferenceWorkflows.ForkJoinResume();
        await fixture.PublishAsync(executable);

        var executionId = string.Empty;
        await measure(async () => executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId));

        var bookmark = Assert.Single(await fixture.ListBookmarksAsync(executionId));
        Assert.Equal(ReferenceWorkflows.EventNodeId, bookmark.ExecutableNodeId);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowExecutionAsync(executionId)).Status);

        (string Id, ActivityExecutionStatus Status)[]? beforeWrites = null;
        if (longSequence)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var writes = (await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId))
                .Where(activity => activity.Execution.ExecutableNodeId.StartsWith("write-", StringComparison.Ordinal)).ToArray();
            Assert.Equal(Enumerable.Range(1, 100).Select(index => $"write-{index}").OrderBy(id => id, StringComparer.Ordinal),
                writes.Select(activity => activity.Execution.ExecutableNodeId).OrderBy(id => id, StringComparer.Ordinal));
            Assert.All(writes, activity => Assert.Equal(ActivityExecutionStatus.Completed, activity.Status));
            beforeWrites = writes.Select(activity => (activity.Execution.ActivityExecutionId, activity.Status)).OrderBy(activity => activity.ActivityExecutionId, StringComparer.Ordinal).ToArray();
        }

        BookmarkResumeDispatchResult? resumed = null;
        await measure(async () =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    workflowExecutionId: executionId,
                    stimulusType: bookmark.StimulusType,
                    stimulusHash: bookmark.StimulusHash,
                    input: JsonSerializer.SerializeToElement(new { eventName = ReferenceWorkflows.EventName })));
        });

        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed!.Status);
        Assert.Empty(await fixture.ListBookmarksAsync(executionId));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        if (beforeWrites is not null)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var afterWrites = (await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId))
                .Where(activity => activity.Execution.ExecutableNodeId.StartsWith("write-", StringComparison.Ordinal))
                .Select(activity => (activity.Execution.ActivityExecutionId, activity.Status)).OrderBy(activity => activity.ActivityExecutionId, StringComparer.Ordinal).ToArray();
            Assert.Equal(beforeWrites, afterWrites);
        }
    }

    /// <summary>
    /// The run's record says it completed. Without <paramref name="recordsCompletion"/> a run that was never committed
    /// leaves no record at all, which is also accepted; a record that exists must still say completed.
    /// </summary>
    private static async Task AssertCompletedAsync(HttpEndpointHostFixture fixture, bool recordsCompletion, string? executionId = null)
    {
        var executions = (await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync())
            .Where(execution => executionId is null || execution.WorkflowExecutionId == executionId)
            .ToArray();
        if (!recordsCompletion && executions.Length == 0)
            return;
        Assert.Equal(WorkflowExecutionStatus.Completed, Assert.Single(executions).Status);
    }

    /// <summary>
    /// Checks the run left nothing for a background pump to do after the count was taken, which that count would miss: no
    /// queued scheduler work, no post-commit work short of delivered, and no incident.
    /// </summary>
    private static async Task AssertSettledAsync(HttpEndpointHostFixture fixture)
    {
        await AssertNothingLeftToDrainAsync(fixture);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().IncidentStates.AsNoTracking().ToArrayAsync());
    }

    /// <summary>No scheduler work is queued and every post-commit outbox item is delivered.</summary>
    internal static async Task AssertNothingLeftToDrainAsync(HttpEndpointHostFixture fixture)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        Assert.All(
            await context.RuntimePostCommitOutbox.AsNoTracking().Select(item => item.Status).ToArrayAsync(),
            status => Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (RuntimePostCommitOutboxStatus)status));
    }
}
