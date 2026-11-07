using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Activities;

/// <summary>Commits an independent durable submission and continues without waiting for its handler result.</summary>
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
public sealed class SubmitJob(IWorkflowExecutionStateStore workflows, IWorkflowExecutableStore executables,
    IActivityExecutionStateStore activities, IPersistenceAccessContextAccessor access,
    IEnumerable<IJobWorkflowAssociationSource> associations, IEnumerable<IJobDataPolicy> dataPolicies) : Activity<SubmitJobResult>
{
    public const string ActivityType = JobsNativeSubmissionBoundary.ActivityType;
    public const string PinMetadataKey = JobsNativeBoundary.PinMetadataKey;
    private static readonly JsonSerializerOptions LiteralJson = new(JsonSerializerDefaults.Web);

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
    public JobAssociationMode AssociationMode { get; set; } = JobAssociationMode.Independent;

    protected override async ValueTask<ActivityTransition<SubmitJobResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var activity = await ReadAuthority(() => activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken), context.CancellationToken)
            ?? throw Unavailable();
        var parent = await ReadAuthority(() => workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken), context.CancellationToken)
            ?? throw Unavailable();
        PersistenceScope partition;
        try { partition = access.Current.RequireScope(); }
        catch (InvalidOperationException error) { throw Unavailable(error); }
        if (activity.Execution.ActivityType != ActivityType || activity.Execution.WorkflowExecutionId != context.WorkflowExecutionId ||
            activity.Execution.ExecutableNodeId != context.ExecutableNodeId || activity.OperationKey != context.OperationKey ||
            parent.WorkflowExecutionId != context.WorkflowExecutionId || parent.Partition?.Value != partition.Value ||
            activity.ContractIdentity is not { ActivityTypeKey: ActivityType } contract) throw Unavailable(JobsNativeBoundary.Invalid());
        var executable = await ReadAuthority(() => executables.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken), context.CancellationToken)
            ?? throw Unavailable();
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
            if (AssociationMode is not (JobAssociationMode.Independent or JobAssociationMode.Detached) ||
                !JobsNativeBoundary.Equivalent(pin, new JobWorkflowHandlerPin(1, Handler, InputSchema, ResultSchema, Queue, Policy, AssociationMode)))
                throw JobsNativeBoundary.Invalid();
            Fixed(node, nameof(Handler), Handler); Fixed(node, nameof(InputSchema), InputSchema); Fixed(node, nameof(ResultSchema), ResultSchema);
            Fixed(node, nameof(Queue), Queue); Fixed(node, nameof(Policy), Policy); Fixed(node, nameof(AssociationMode), AssociationMode);
        }
        catch (JobException error) { throw Unavailable(error); }
        JobPayload input;
        try
        {
            input = new(InputSchema, Input);
            if (JobPayloadLimits.GetByteCount(input) > JobPayloadLimits.MaximumBytes) throw JobsNativeBoundary.Invalid();
            var prepared = new JobRequest(Handler, input, ResultSchema, Queue, Policy, NotBefore?.ToUniversalTime(), null);
            foreach (var policy in dataPolicies) policy.ValidateAdmission(new(partition.Value, parent.TenantId), prepared);
        }
        catch (Exception error) when (error is ArgumentException or JobDataPolicyException || error is JobException { Code: "jobs.payload.invalid" or "jobs.native.invalid" })
        {
            return ActivityTransition.Fault<SubmitJobResult>(new(JobsNativeBoundary.PreAdmissionFaultCode,
                "The Jobs input cannot be accepted by the supported payload contract.",
                faultType: JobsNativeBoundary.PreAdmissionMarker(parent, activity, context.AttemptId)));
        }
        var owner = await ReadAuthority(() => new JobsNativeOwnerResolver(associations).ResolveAsync(parent, activity, context.CancellationToken), context.CancellationToken);
        var submission = new JobNativeSubmission(1, new(partition.Value, parent.TenantId), parent.PinnedExecutable,
            context.WorkflowExecutionId, context.InvocationId, context.OperationKey, context.AttemptId, context.ExecutableNodeId,
            contract, pin, input, NotBefore?.ToUniversalTime(), owner);
        try { return ActivityTransition.Complete(JobsNativeSubmissionBoundary.Receipt(submission)); }
        catch (JobException error) { throw Unavailable(error); }
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

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Exception? error = null) => new("jobs.native.submission-unavailable",
        "The exact native Jobs submission is not currently available.", error);
}
