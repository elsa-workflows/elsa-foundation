using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class UserTaskRuntimeJourneyTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Open_and_accepted_tasks_survive_restart_and_complete_exactly_one_wait(PersistenceMode mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-user-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "runtime.db"); string taskId;
        try
        {
            await using (var first = Build(database, mode, "first"))
            {
                var source = await first.PublishAsync(Executable(), "task-published");
                await first.StartPublishedAsync(source, "user-task-run", tenantId: "default", partition: new("default"));
                await first.SweepUntilQuietAsync();
                var staged = Assert.Single((await first.Services.GetRequiredService<IUserTaskStore>().RecoverAsync(10, null)).Items);
                taskId = staged.Id;
                await first.Services.GetRequiredService<IUserTaskRuntime>().ReconcileAsync(staged);
                var available = await first.Services.GetRequiredService<IUserTaskOperations>().GetAsync(taskId);
                Assert.Equal(UserTaskStatus.Open, available.Status);
                Assert.NotNull(available.Recipient?.RegistrationCapture);
            }
            // New provider/context generation over the same database, without re-running the activity.
            await using (var second = Build(database, mode, "second"))
            {
                var tasks = second.Services.GetRequiredService<IUserTaskOperations>();
                var task = await tasks.GetAsync(taskId);
                var accepted = await tasks.CompleteAsync(taskId, new(task.Revision, "purchase-response", "Approve", JsonSerializer.SerializeToElement(new { })));
                Assert.Equal(UserTaskStatus.CompletionPending, accepted.Status);
            }
            // Crash after durable response acceptance, before any explicit delivery/reconciliation tick.
            await using (var third = Build(database, mode, "third"))
            {
                var store = third.Services.GetRequiredService<IUserTaskStore>();
                var pending = (await store.FindAsync("default", taskId))!;
                Assert.Equal(UserTaskStatus.CompletionPending, pending.Status);
                var runtime = third.Services.GetRequiredService<IUserTaskRuntime>();
                await runtime.ReconcileAsync(pending);
                await third.SweepUntilQuietAsync();
                await runtime.ReconcileAsync(pending); // acknowledgement reads canonical committed completion, then replay is inert.
                var completed = await store.FindAsync("default", taskId);
                Assert.Equal(UserTaskStatus.Completed, completed!.Status);
                Assert.Equal("Approve", completed.Decision!.Response.Action);
                var state = await third.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync("user-task-run", completed.InvocationId);
                Assert.Equal(ActivityExecutionStatus.Completed, state!.Status);
                Assert.Equal("Approve", state.Completion!.OutcomeKey);
                Assert.Single(completed.History.Where(x => x.Operation == "accepted"));
                Assert.Single(completed.History.Where(x => x.Operation == "completed"));
                Assert.Empty((await third.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new("user-task-run"))).Items);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private static WorkflowExecutionHarness Build(string database, PersistenceMode mode, string generation)
    {
        var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRuntimeEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    RecoveryContinuationSigningKey = "user-task-recovery-key-at-least-32-bytes", HierarchyCursorSigningKey = "user-task-hierarchy-key-at-least-32-bytes" });
                services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                services.Replace(ServiceDescriptor.Scoped<IUserTaskAccess, ActorAccess>());
            }).WithPersistenceMode(mode).Build(WorkflowExecutionHarness.Identity, "user-task-run", Enumerable.Range(1, 30).Select(x => $"task-{generation}-{x}"));
        foreach (var initializer in host.Services.GetServices<IShellInitializer>()) initializer.InitializeAsync().GetAwaiter().GetResult();
        host.InitializeActivityTypes();
        return host;
    }

    private static WorkflowExecutable Executable()
    {
        var original = ClrActivityContractTestBuilder.BuildContract(typeof(CreateUserTask));
        var contract = new ActivityContract(original.ActivityTypeKey, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
            original.Inputs.Values, original.Result, ["Approve", "Reject", "Unmatched"], original.Activation);
        RuntimeInputBinding Literal(string key, object value) => new(key, contract.Inputs[key].Type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(contract.Inputs[key].Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode("task-node", "authored-task", UserTaskProtocol.ActivityType, "1.0.0", contract.DescriptorKind, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(CreateUserTask.Title)] = Literal(nameof(CreateUserTask.Title), "Purchase approval"),
                [nameof(CreateUserTask.AssigneeId)] = Literal(nameof(CreateUserTask.AssigneeId), "alice"),
                [nameof(CreateUserTask.Actions)] = Literal(nameof(CreateUserTask.Actions), new[] { "Approve", "Reject" }),
                [nameof(CreateUserTask.OptionalFormActions)] = Literal(nameof(CreateUserTask.OptionalFormActions), Array.Empty<string>()),
                [nameof(CreateUserTask.Prefill)] = Literal(nameof(CreateUserTask.Prefill), new { })
            }), new Dictionary<string, string>(), activityContract: contract);
        return new(WorkflowExecutionHarness.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [UserTaskProtocol.ResumeTarget] = new(UserTaskProtocol.ResumeTarget, "task-node", "ResumeAsync", new Dictionary<string, string>())
        }, WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class ActorAccess : IUserTaskAccess
    {
        public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new UserTaskActor("default", "alice", new HashSet<string>(), true, true, true, true, true));
        public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
