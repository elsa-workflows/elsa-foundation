using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;

namespace Elsa.Etl.Workflows.Runtime.Activities;

/// <summary>Waits for one pinned finite ETL run through the normal typed native activity boundary.</summary>
[Activity(Icon = "Workflow")]
[ActivityOutcome("Done"), ActivityOutcome("BusinessError")]
[RefusesSecretBinding(nameof(Pipeline), SecretBindingRefusalReason.FixedAtPublish)]
public sealed class RunEtl(IWorkflowExecutionStateStore workflows, IWorkflowExecutableStore executables,
    IActivityExecutionStateStore activities, IEtlRuntimeStore runs, IPersistenceAccessContextAccessor access)
    : StatefulActivity<RunEtlResult, RunEtlState, RunEtlCompletion>
{
    public const string ActivityType = "Elsa.Etl.Workflows.Runtime.Activities.RunEtl";

    [ActivityInput(Key = nameof(Pipeline), Order = 0)]
    [Required]
    public string Pipeline { get; set; } = string.Empty;

    protected override async ValueTask<ActivityTransition<RunEtlResult, RunEtlState>> ExecuteAsync(ActivityExecutionContext context)
    {
        var activity = await activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken)
            ?? throw EtlNativeBoundary.Invalid();
        var parent = await workflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken)
            ?? throw EtlNativeBoundary.Invalid();
        var persistenceScope = access.Current.RequireScope();
        if (activity.Execution.ActivityType != ActivityType || activity.Execution.WorkflowExecutionId != context.WorkflowExecutionId ||
            activity.InvocationId != context.InvocationId || activity.Execution.ExecutableNodeId != context.ExecutableNodeId ||
            parent.Partition?.Value != persistenceScope.Value)
            throw EtlNativeBoundary.Invalid();
        var executable = await executables.FindAsync(parent.PinnedExecutable.ArtifactId, context.CancellationToken)
            ?? throw EtlNativeBoundary.Invalid();
        if (executable.Identity.ArtifactId != parent.PinnedExecutable.ArtifactId || executable.Identity.ArtifactHash != parent.PinnedExecutable.ArtifactHash ||
            !executable.NodesById.TryGetValue(context.ExecutableNodeId, out var node) ||
            node.ActivityType != ActivityType ||
            !node.Metadata.TryGetValue(EtlNativeBoundary.PinKey, out var pin))
            throw EtlNativeBoundary.Invalid();
        var selection = node.InputBindings.GetValueOrDefault(nameof(Pipeline));
        if (selection is not { Source: RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: JsonValueKind.String } literal } ||
            literal.GetString() != Pipeline)
            throw EtlNativeBoundary.Invalid();
        var artifact = EtlNativeBoundary.ReadPin(pin, literal.GetString()!);
        var scope = new EtlScope(parent.TenantId, persistenceScope.Value);
        var runId = EtlNativeBoundary.RunId(scope, context.WorkflowExecutionId, context.InvocationId);
        var request = new RunEtlRequest(scope, runId, artifact, context.WorkflowExecutionId, context.InvocationId,
            parent.PinnedExecutable.ArtifactId, parent.PinnedExecutable.ArtifactHash, context.ExecutableNodeId);
        return Suspend(new(EtlNativeBoundary.FormatVersion, EtlRuntimeJson.WriteElement(request)),
            [new ActivityTriggerRegistration<RunEtlCompletion>(EtlNativeBoundary.ResumeTargetKey, EtlNativeBoundary.StimulusType, runId)]);
    }

    protected override async ValueTask<ActivityTransition<RunEtlResult, RunEtlState>> ResumeAsync(
        ActivityResumeContext<RunEtlState, RunEtlCompletion> context)
    {
        var request = EtlNativeBoundary.ReadState(context.State);
        if (context.Trigger.FormatVersion != EtlNativeBoundary.FormatVersion ||
            context.WorkflowExecutionId != request.WorkflowExecutionId || context.InvocationId != request.ActivityExecutionId ||
            context.ExecutableNodeId != request.NodeId)
            throw EtlNativeBoundary.Invalid();
        access.Current.EnsureScope(new(request.Scope.PersistencePartition));
        var invocation = EtlRuntimeJson.Read<EtlInvocation>(context.Trigger.Invocation, EtlNativeBoundary.MaximumRequestBytes);
        var result = EtlRuntimeJson.Read<EtlTerminalResult>(context.Trigger.Result, EtlNativeBoundary.MaximumResultBytes);
        var persisted = await runs.FindRunAsync(request.Scope, request.RunId, context.CancellationToken);
        if (persisted is null || persisted.TerminalResult != result ||
            EtlRuntimeIdentity.InvocationFingerprint(persisted.Invocation) != EtlRuntimeIdentity.InvocationFingerprint(invocation) ||
            EtlRuntimeIdentity.InvocationFingerprint(EtlNativeBoundary.Invocation(request, context.RegistrationId)) !=
            EtlRuntimeIdentity.InvocationFingerprint(invocation))
            throw EtlNativeBoundary.Invalid();
        var value = new RunEtlResult(request.RunId, result.Outcome, result.AcceptedRecords,
            result.RejectedRecords, result.FilteredRecords, result.Diagnostic?.Code);
        return result.Outcome switch
        {
            EtlTerminalOutcome.Succeeded => Complete(value),
            EtlTerminalOutcome.BusinessError => Complete(value, "BusinessError"),
            EtlTerminalOutcome.Faulted => Fault(new(result.Diagnostic?.Code ?? "etl.run-faulted", "The ETL run failed.")),
            EtlTerminalOutcome.Cancelled => Cancel("The ETL run was cancelled."),
            _ => throw EtlNativeBoundary.Invalid()
        };
    }
}
