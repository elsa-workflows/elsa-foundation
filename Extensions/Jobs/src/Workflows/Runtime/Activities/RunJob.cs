using System.Text.Json;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Activities;

/// <summary>Awaits one durable handler invocation through a native saved wait, without executing the handler in workflow activation.</summary>
[Activity(Icon = "Workflow")]
[ActivityOutcome("Done")]
[RefusesSecretBinding(nameof(Handler), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(InputSchema), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(ResultSchema), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(Queue), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(Policy), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(AssociationMode), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(Input), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(NotBefore), SecretBindingRefusalReason.PersistedByActivity)]
public sealed class RunJob : StatefulActivity<RunJobResult, RunJobState, RunJobCompletion>
{
    public const string ActivityType = JobsNativeBoundary.ActivityType;
    public const string PinMetadataKey = JobsNativeBoundary.PinMetadataKey;
    private readonly IWorkflowExecutionStateStore _workflows;
    private readonly IWorkflowExecutableStore _executables;
    private readonly IActivityExecutionStateStore _activities;
    private readonly IPersistenceAccessContextAccessor _access;
    private readonly JobsNativeOwnerResolver _owners;
    private readonly JobsNativeResultValidator _results;
    private static readonly JsonSerializerOptions LiteralJson = new(JsonSerializerDefaults.Web);

    public RunJob(IWorkflowExecutionStateStore workflows, IWorkflowExecutableStore executables,
        IActivityExecutionStateStore activities, IPersistenceAccessContextAccessor access,
        IEnumerable<Contracts.IJobWorkflowAssociationSource> associations, IJobQueries jobs)
    {
        _workflows = workflows; _executables = executables; _activities = activities; _access = access;
        _owners = new(associations); _results = new(jobs);
    }

    [ActivityInput(Key = nameof(Handler), Order = 0), Required]
    public JobHandlerIdentity Handler { get; set; } = new(string.Empty, string.Empty);
    [ActivityInput(Key = nameof(InputSchema), Order = 1), Required]
    public JobSchema InputSchema { get; set; } = new(string.Empty, 1);
    [ActivityInput(Key = nameof(ResultSchema), Order = 2), Required]
    public JobSchema ResultSchema { get; set; } = new(string.Empty, 1);
    [ActivityInput(Key = nameof(Input), Order = 3)]
    public JsonElement Input { get; set; } = JsonSerializer.SerializeToElement<object?>(null);
    [ActivityInput(Key = nameof(Queue), Order = 4)]
    public string Queue { get; set; } = "default";
    [ActivityInput(Key = nameof(Policy), Order = 5), Required]
    public JobExecutionPolicy Policy { get; set; } = new(new(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 2, 0, true),
        TimeSpan.FromMinutes(5), TimeSpan.FromDays(30));
    [ActivityInput(Key = nameof(NotBefore), Order = 6)]
    public DateTimeOffset? NotBefore { get; set; }
    [ActivityInput(Key = nameof(AssociationMode), Order = 7)]
    public JobAssociationMode AssociationMode { get; set; } = JobAssociationMode.Awaited;

    protected override async ValueTask<ActivityTransition<RunJobResult, RunJobState>> ExecuteAsync(ActivityExecutionContext context)
    {
        var activity = await ReadAuthority(() => _activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken), context.CancellationToken)
            ?? throw Unavailable();
        var parent = await ReadAuthority(() => _workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken), context.CancellationToken) ?? throw Unavailable();
        PersistenceScope partition;
        try { partition = _access.Current.RequireScope(); }
        catch (InvalidOperationException error) { throw Unavailable(error); }
        if (activity.Execution.ActivityType != ActivityType || activity.Execution.WorkflowExecutionId != context.WorkflowExecutionId ||
            activity.Execution.ExecutableNodeId != context.ExecutableNodeId || activity.OperationKey != context.OperationKey ||
            parent.WorkflowExecutionId != context.WorkflowExecutionId || parent.Partition?.Value != partition.Value ||
            activity.ContractIdentity is not { ActivityTypeKey: ActivityType } contract) throw Unavailable(JobsNativeBoundary.Invalid());
        var executable = await ReadAuthority(() => _executables.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken), context.CancellationToken) ?? throw Unavailable();
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, parent.PinnedExecutable) ||
            !executable.NodesById.TryGetValue(context.ExecutableNodeId, out var node) || node.ActivityType != ActivityType ||
            !node.Metadata.TryGetValue(PinMetadataKey, out var descriptor) || node.ActivityContract is not { } nodeContract ||
            activity.Execution.ActivityTypeVersion != node.ActivityTypeVersion || contract.ActivityTypeKey != nodeContract.ActivityTypeKey ||
            contract.ContractVersion != nodeContract.ContractVersion || contract.SchemaFingerprint != nodeContract.SchemaFingerprint)
            throw Unavailable(JobsNativeBoundary.Invalid());
        JobWorkflowHandlerPin pin;
        try
        {
            pin = JobWorkflowHandlerPin.Parse(descriptor);
            if (!JobsNativeBoundary.Equivalent(pin, new JobWorkflowHandlerPin(1, Handler, InputSchema, ResultSchema, Queue, Policy, AssociationMode)))
                throw JobsNativeBoundary.Invalid();
            Fixed(node, nameof(Handler), Handler); Fixed(node, nameof(InputSchema), InputSchema); Fixed(node, nameof(ResultSchema), ResultSchema);
            Fixed(node, nameof(Queue), Queue); Fixed(node, nameof(Policy), Policy); Fixed(node, nameof(AssociationMode), AssociationMode);
        }
        catch (JobException error) { throw Unavailable(error); }
        var target = WorkflowExecutableResumeTarget.FindForNode(executable, node.ExecutableNodeId, JobsNativeBoundary.ResumeTargetKey);
        if (target is null || target.ExecutableNodeId != node.ExecutableNodeId || target.HandlerKey != nameof(ResumeAsync) ||
            !executable.ResumeTargets.TryGetValue(target.ResumeTargetId, out var declared) || declared != target) throw Unavailable(JobsNativeBoundary.Invalid());
        JobPayload input;
        try
        {
            input = new(InputSchema, Input);
            if (JobPayloadLimits.GetByteCount(input) > JobPayloadLimits.MaximumBytes) throw JobsNativeBoundary.Invalid();
        }
        catch (Exception error) when (error is ArgumentException || error is JobException { Code: "jobs.payload.invalid" or "jobs.native.invalid" })
        {
            // Positive initial-phase failure. No saved wait/start source or JobId has ever been created.
            return Fault(new(JobsNativeBoundary.PreAdmissionFaultCode, "The Jobs input cannot be accepted by the supported payload contract.",
                faultType: JobsNativeBoundary.PreAdmissionMarker(parent, activity, context.AttemptId)));
        }
        var owner = await ReadAuthority(() => _owners.ResolveAsync(parent, activity, context.CancellationToken), context.CancellationToken);
        var invocation = new RunJobInvocation(1, new(partition.Value, parent.TenantId), parent.PinnedExecutable, context.WorkflowExecutionId,
            context.InvocationId, context.OperationKey, context.AttemptId, context.ExecutableNodeId, contract, target.ResumeTargetId,
            pin, input, NotBefore?.ToUniversalTime(), owner);
        try { JobsNativeBoundary.ValidateInvocation(invocation); }
        catch (JobException error) { throw Unavailable(error); }
        return Suspend(new(1, JobsNativeBoundary.Write(invocation)), [new ActivityTriggerRegistration<RunJobCompletion>(
            JobsNativeBoundary.ResumeTargetKey, JobsNativeBoundary.StimulusType, JobsNativeBoundary.StimulusHash(invocation))]);
    }

    [ResumeTarget(JobsNativeBoundary.ResumeTargetKey)]
    protected override async ValueTask<ActivityTransition<RunJobResult, RunJobState>> ResumeAsync(ActivityResumeContext<RunJobState, RunJobCompletion> context)
    {
        try { return await ResumeVerifiedAsync(context); }
        catch (JobException error) when (error.Code is "jobs.native.invalid" or "jobs.payload.invalid" or "jobs.request.invalid")
        { throw Unavailable(error); }
    }

    private async ValueTask<ActivityTransition<RunJobResult, RunJobState>> ResumeVerifiedAsync(ActivityResumeContext<RunJobState, RunJobCompletion> context)
    {
        var invocation = JobsNativeBoundary.ReadState(context.State);
        if (context.WorkflowExecutionId != invocation.WorkflowExecutionId || context.InvocationId != invocation.InvocationId ||
            context.ExecutableNodeId != invocation.NodeId || context.OperationKey != invocation.OperationKey) throw JobsNativeBoundary.Invalid();
        _access.Current.EnsureScope(new(invocation.Scope.PersistencePartition));
        var activity = await ReadAuthority(() => _activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken), context.CancellationToken) ?? throw Unavailable();
        var admission = JobsNativeBoundary.ReadAdmission(activity);
        if (!JobsNativeBoundary.Equivalent(admission.Invocation, invocation) || context.RegistrationId != admission.Address.RegistrationId ||
            activity.TriggerDeliveries?.SingleOrDefault(item => item.DeliveryId == context.DeliveryId) is not { } receipt ||
            receipt.Status != ActivityTriggerDeliveryStatus.Consumed || receipt.RegistrationId != context.RegistrationId ||
            receipt.ProviderId != JobsNativeBoundary.ProviderId || receipt.PayloadType != JobsNativeBoundary.CompletionType ||
            receipt.DeduplicationKey != JobsNativeBoundary.DeduplicationKey(admission.Address,
                JobsNativeBoundary.DeliveryId(admission.Address, context.Trigger))) throw JobsNativeBoundary.Invalid();
        var result = await _results.ValidateAsync(admission, context.Trigger, context.CancellationToken);
        return result.Outcome switch
        {
            JobOutcome.Succeeded => Complete(JobsNativeBoundary.Result(context.Trigger, result)),
            JobOutcome.Failed or JobOutcome.TimedOut => Fault(new(result.Diagnostic?.Code ?? "jobs.job.failed", "The selected Jobs invocation failed.",
                faultType: JobsNativeBoundary.TerminalMarker(context.Trigger))),
            JobOutcome.Cancelled => Cancel("The selected Jobs invocation was cancelled."),
            _ => throw JobsNativeBoundary.Invalid()
        };
    }

    private static void Fixed<T>(ExecutableNode node, string key, T materialized)
    {
        if (node.InputBindings.GetValueOrDefault(key) is not { Source: RuntimeInputBindingSource.Literal, LiteralValue: { } literal })
            throw JobsNativeBoundary.Invalid();
        var authored = JobsNativeBoundary.Protect(() => literal.Deserialize<T>(LiteralJson));
        if (!JobsNativeBoundary.Equivalent(authored, materialized)) throw JobsNativeBoundary.Invalid();
    }

    private static async ValueTask<T> ReadAuthority<T>(Func<ValueTask<T>> read, CancellationToken cancellationToken)
    {
        try { return await read(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { throw Unavailable(error); }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Exception? error = null) => new("jobs.native.unavailable",
        "The saved native Jobs invocation is not currently available.", error);
}
