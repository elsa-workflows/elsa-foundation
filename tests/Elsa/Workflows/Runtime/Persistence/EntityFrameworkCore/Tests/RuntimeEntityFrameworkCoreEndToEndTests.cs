using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Drives a real workflow through the runtime on the EF Runtime aggregate alone: no Groundwork anywhere, one SQLite
/// file, schema installed by the registered module migrator. Every generation is a new service provider over that
/// file, so a generation can only observe what an earlier one committed to the database.
/// </summary>
public sealed class RuntimeEntityFrameworkCoreEndToEndTests : IDisposable
{
    private const string EventName = "order-shipped";
    private const string ExecutableNodeId = "node-event";
    private const string ActivityExecutionId = "actexec-event";
    private const string RecoverySigningKey = "ef-runtime-end-to-end-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-end-to-end-hierarchy-signing-key-32";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-e2e-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_workflow_suspended_on_a_bookmark_survives_restarts_then_resumes_to_completion()
    {
        await using (var first = await StartGenerationAsync())
        {
            await first.RunAsync(NewEventExecutable());

            Assert.Single(await BookmarksAsync(first));
            Assert.Equal(ActivityExecutionStatus.Suspended, await ActivityStatusAsync(first));
            // A workflow waiting on a bookmark stays Running; only its activity is suspended.
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(first));
        }

        await using (var second = await StartGenerationAsync())
        {
            var bookmark = Assert.Single(await BookmarksAsync(second));
            Assert.Equal(ActivityExecutionStatus.Suspended, await ActivityStatusAsync(second));
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(second));

            await using var scope = second.Services.CreateAsyncScope();
            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    workflowExecutionId: second.ExecutionId,
                    stimulusType: bookmark.StimulusType,
                    stimulusHash: bookmark.StimulusHash,
                    input: JsonSerializer.SerializeToElement(new EventReceived(EventName))));

            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);
        }

        await using var third = await StartGenerationAsync();
        Assert.Empty(await BookmarksAsync(third));
        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(third));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(third));
        await using (var scope = third.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }

    /// <summary>Builds a fresh provider over the database file and installs or validates its schema, as shell activation does.</summary>
    private async Task<WorkflowExecutionHarness> StartGenerationAsync()
    {
        var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services
                .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                })
                .AddEfModuleMigrations<BookmarkStateDbContext>("Sqlite"))
            .Build(WorkflowExecutionHarness.Identity, WorkflowExecutionHarness.WorkflowExecutionId, [ActivityExecutionId]);

        var initializers = harness.Services.GetServices<IShellInitializer>().ToArray();
        Assert.Contains(initializers, initializer => initializer is EfModuleMigrator<BookmarkStateDbContext>);
        foreach (var initializer in initializers)
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        return harness;
    }

    private static async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(harness.ExecutionId);
    }

    private static async Task<ActivityExecutionStatus> ActivityStatusAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return Assert.Single(await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId)).Status;
    }

    private static async Task<WorkflowExecutionStatus?> WorkflowStatusAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        Assert.IsType<EfWorkflowExecutionStateStore>(store);
        return (await store.FindAsync(harness.ExecutionId))?.Status;
    }

    /// <summary>A mid-flow <see cref="Event"/> wait: it suspends with a trigger registration and completes on the named event.</summary>
    private static WorkflowExecutable NewEventExecutable()
    {
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(ExecutableNodeId, Event.ResumeTargetId);
        using var descriptor = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: ExecutableNodeId,
            authoredActivityId: "authored-node-event",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptor.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = Literal(nameof(Event.EventName), "String", EventName),
                [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), "Boolean", false)
            },
            metadata: new Dictionary<string, string>());
        return new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
            },
            WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static RuntimeInputBinding Literal(string inputName, string typeAlias, object value)
    {
        var type = new ValueTypeDescriptor(typeAlias);
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }
}
