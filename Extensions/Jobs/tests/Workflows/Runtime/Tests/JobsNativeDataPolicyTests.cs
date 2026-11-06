using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using ActivityTransitionKind = Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativeDataPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepared_input_policy_runs_before_saved_wait_and_known_refusal_is_a_positive_initial_fault(bool refuse)
    {
        var pin = NativeFixture.Pin; var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var contract = new ActivityContract(RunJob.ActivityType, "1", "test", JsonSerializer.SerializeToElement(new { }), [],
            new(new("test-result"), false, ActivityValuePolicy.Default, []), ["Done"], new("test", "test"));
        RuntimeInputBinding Literal<T>(string key, T value) => new(key, new("literal"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new("literal"), JsonSerializer.SerializeToElement(value, json), ValueProtectionPolicy.InstanceInline));
        var bindings = new Dictionary<string, RuntimeInputBinding> { [nameof(RunJob.Handler)] = Literal(nameof(RunJob.Handler), pin.Handler), [nameof(RunJob.InputSchema)] = Literal(nameof(RunJob.InputSchema), pin.InputSchema),
            [nameof(RunJob.ResultSchema)] = Literal(nameof(RunJob.ResultSchema), pin.ResultSchema), [nameof(RunJob.Queue)] = Literal(nameof(RunJob.Queue), pin.Queue), [nameof(RunJob.Policy)] = Literal(nameof(RunJob.Policy), pin.Policy),
            [nameof(RunJob.AssociationMode)] = Literal(nameof(RunJob.AssociationMode), pin.AssociationMode) };
        var node = new ExecutableNode("node-one", "authored-one", RunJob.ActivityType, "1", "test", JsonSerializer.SerializeToElement(new { }), bindings,
            new Dictionary<string, string> { [RunJob.PinMetadataKey] = pin.Serialize() }, activityContract: contract);
        var target = new WorkflowExecutableResumeTarget("actual-opaque-resume-target", "node-one", "ResumeAsync", new Dictionary<string, string>(), JobsNativeBoundary.ResumeTargetKey);
        var executable = new WorkflowExecutable(NativeFixture.Executable, node, new Dictionary<string, WorkflowExecutableResumeTarget> { [target.ResumeTargetId] = target }, NativeFixture.Now, NativeFixture.Metadata, IncidentStrategyBuiltIns.FaultReference);
        var activity = NativeFixture.Activity() with { Status = ActivityExecutionStatus.Running, PrivateState = null, BookmarkIds = [], TriggerRegistrations = [],
            ContractIdentity = new(RunJob.ActivityType, "1", contract.SchemaFingerprint) };
        var activities = new ActivityStore(); activities.States.Add(activity.InvocationId, activity); var transport = new JobTransport(); var policy = new InputPolicy(refuse);
        var run = new RunJob(new WorkflowStore(NativeFixture.Parent()), new ReadOnlyExecutable(executable), activities, new Access(), [], transport, [policy])
        { Handler = pin.Handler, InputSchema = pin.InputSchema, ResultSchema = pin.ResultSchema, Queue = pin.Queue, Policy = pin.Policy, AssociationMode = pin.AssociationMode,
            Input = JsonSerializer.SerializeToElement(new { Count = 42 }) };
        var transition = await ((IStatefulActivity<RunJobResult, RunJobState, RunJobCompletion>)run).ExecuteAsync(new("workflow-one", "activity-one", "initial-attempt", "node-one", CancellationToken.None));
        Assert.Equal(42, policy.Request!.Input.Value.GetProperty("Count").GetInt32()); Assert.Equal(NativeFixture.Scope, policy.Scope); Assert.Equal(1, policy.Reads);
        Assert.Empty(transport.Admissions); Assert.Null(activities.States[activity.InvocationId].PrivateState); Assert.Empty(activities.States[activity.InvocationId].TriggerRegistrations!);
        if (refuse)
        {
            Assert.Equal(ActivityTransitionKind.Fault, transition.Kind); var fault = Assert.IsAssignableFrom<IActivityFaultTransition>(transition).Fault;
            Assert.Equal(JobsNativeBoundary.PreAdmissionFaultCode, fault.Code); Assert.DoesNotContain("Count", fault.Message);
            Assert.Equal(JobsNativeBoundary.PreAdmissionMarker(NativeFixture.Parent(), activity, "initial-attempt"), fault.FaultType); Assert.False(fault.IsRetryable);
        }
        else { Assert.Equal(ActivityTransitionKind.Suspend, transition.Kind); Assert.Equal(42, JobsNativeBoundary.Read<RunJobInvocation>(Assert.IsAssignableFrom<IActivitySuspensionTransition<RunJobState>>(transition).State.Invocation).Input.Value.GetProperty("Count").GetInt32()); }
    }
    private sealed class InputPolicy(bool refuse) : IJobDataPolicy
    {
        internal int Reads; internal JobScope? Scope; internal JobRequest? Request;
        public void ValidateAdmission(JobScope scope, JobRequest request) { Reads++; Scope = scope; Request = request; if (refuse) throw new JobDataPolicyException(); }
        public void BeginInvocation(JobExecutionContext context, JobRequest request) => throw new InvalidOperationException("Worker binding is not an admission operation.");
        public void ValidateProgress(JobExecutionContext context, JobProgress progress) => throw new InvalidOperationException();
        public void ValidateResult(JobExecutionContext context, JobPayload? payload) => throw new InvalidOperationException();
    }
    private sealed class ReadOnlyExecutable(WorkflowExecutable executable) : IWorkflowExecutableStore
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowExecutable?>(artifactId == executable.Identity.ArtifactId ? executable : null);
        public ValueTask SaveAsync(WorkflowExecutable value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
