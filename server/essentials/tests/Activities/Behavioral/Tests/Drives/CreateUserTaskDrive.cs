using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Measures accepted task decisions through the real durable wait, journal, delivery and acknowledgement.</summary>
public sealed class CreateUserTaskDrive : IActivityDrive
{
    private const string NodeId = "human-task";
    private static readonly string[] Actions = ["Done", "Approve", "Reject"];
    public Type ActivityType => typeof(CreateUserTask);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        foreach (var action in Actions) await DriveCase(recorder, action, missing: null);
        foreach (var input in new[] { nameof(CreateUserTask.Title), nameof(CreateUserTask.Actions) })
            await DriveCase(recorder, "missing-" + input, missing: input);
    }

    private async Task DriveCase(ActivityDriveRecorder recorder, string action, string? missing)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-task-drive-");
        try
        {
            await using var harness = WorkflowExecutionHarness.Create()
                .WithFeature(services => new Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
                .ConfigureServices(services =>
                {
                    services.AddRuntimeEntityFrameworkCore(new()
                    {
                        Provider = "Sqlite", ConnectionString = $"Data Source={Path.Combine(directory.FullName, "runtime.db")};Pooling=False",
                        RecoveryContinuationSigningKey = "task-drive-recovery-key-at-least-32-bytes",
                        HierarchyCursorSigningKey = "task-drive-hierarchy-key-at-least-32-bytes"
                    });
                    services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                    services.Replace(ServiceDescriptor.Scoped<IUserTaskAccess>(_ => new ActorAccess()));
                }).WithPersistenceMode(PersistenceMode.Durable)
                .Build(WorkflowExecutionHarness.Identity, "task-drive-run", Enumerable.Range(1, 32).Select(x => "task-drive-" + x));
            foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
            var reference = await harness.PublishAsync(Executable(missing), "task-drive-source");
            if (missing is not null)
            {
                try
                {
                    await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: "default", partition: new("default"));
                    await harness.SweepUntilQuietAsync();
                    recorder.RecordRequiredInputFault(ActivityType, missing, await harness.ReadRunAsync(harness.ExecutionId), NodeId);
                }
                catch (InvalidOperationException refusal)
                {
                    recorder.RecordRequiredInputFault(ActivityType, missing, null, NodeId, refusal);
                }
                return;
            }
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: "default", partition: new("default"));
            await harness.SweepUntilQuietAsync();
            var store = harness.Services.GetRequiredService<IUserTaskStore>();
            var runtime = harness.Services.GetRequiredService<IUserTaskRuntime>();
            var staged = Assert.Single((await store.RecoverAsync(10, null)).Items);
            await runtime.ReconcileAsync(staged);
            var operations = harness.Services.GetRequiredService<IUserTaskOperations>();
            var open = await operations.GetAsync(staged.Id);
            Assert.Equal(UserTaskStatus.Open, open.Status);
            var accepted = await operations.CompleteAsync(open.Id, new(open.Revision, "response-" + action, action, JsonSerializer.SerializeToElement(new { })));
            await runtime.ReconcileAsync(accepted); await harness.SweepUntilQuietAsync(); await runtime.ReconcileAsync(accepted);
            Assert.Equal(UserTaskStatus.Completed, (await store.FindAsync("default", open.Id))!.Status);
            var run = await harness.ReadRunAsync(harness.ExecutionId);
            run.AssertWorkflowCompleted();
            recorder.Record(ActivityType, run, NodeId);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static WorkflowExecutable Executable(string? missing)
    {
        var original = ClrActivityContractTestBuilder.BuildContract(typeof(CreateUserTask));
        // The real publication contract derives these per-node ports from the same authored action values.
        var contract = new ActivityContract(original.ActivityTypeKey, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
            original.Inputs.Values, original.Result, Actions.Append("Unmatched").ToArray(), original.Activation);
        RuntimeInputBinding Literal(string key, object? value) => new(key, contract.Inputs[key].Type, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: value is null ? ValueEnvelope.Null(contract.Inputs[key].Type, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(contract.Inputs[key].Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(CreateUserTask.Title)] = Literal(nameof(CreateUserTask.Title), missing == nameof(CreateUserTask.Title) ? null : "Purchase decision"),
            [nameof(CreateUserTask.Actions)] = Literal(nameof(CreateUserTask.Actions), missing == nameof(CreateUserTask.Actions) ? null : Actions),
            [nameof(CreateUserTask.AssigneeId)] = Literal(nameof(CreateUserTask.AssigneeId), "alice"),
            [nameof(CreateUserTask.OptionalFormActions)] = Literal(nameof(CreateUserTask.OptionalFormActions), Array.Empty<string>()),
            [nameof(CreateUserTask.Prefill)] = Literal(nameof(CreateUserTask.Prefill), new { })
        };
        var node = new ExecutableNode(NodeId, "authored-human-task", UserTaskProtocol.ActivityType, "1.0.0", contract.DescriptorKind, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings), new Dictionary<string, string>(), activityContract: contract);
        return new(WorkflowExecutionHarness.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [UserTaskProtocol.ResumeTarget] = new(UserTaskProtocol.ResumeTarget, NodeId, "ResumeAsync", new Dictionary<string, string>())
        }, WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class ActorAccess : IUserTaskAccess
    {
        public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new UserTaskActor("default", "alice", new HashSet<string>(), true, true, true, true, true));
        public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
