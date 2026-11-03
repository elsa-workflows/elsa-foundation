using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

/// <summary>
/// Reads one invocation's evidence through the same native binding the runtime uses, so the identity it reads the journal
/// with is the one the turn was staged under. Any binding mismatch reads as "no specialist evidence", never as a guess.
/// </summary>
internal sealed class SpecialistInvocationInspector(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables, ISpecialistTurnStore turns, IPersistenceAccessContextAccessor access,
    IRuntimePayloadCapturePolicy capture, TimeProvider time) : ISpecialistInvocationInspector
{
    public async ValueTask<SpecialistInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        var scope = access.Current.RequireScope();
        var workflow = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || workflow.Partition?.Value != scope.Value || workflow.TenantId is { } tenant && new PersistenceScope(tenant) != scope) return null;
        var state = await activities.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        if (state is null) return null;
        var executable = await executables.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || executable.Identity.ArtifactHash != workflow.PinnedExecutable.ArtifactHash ||
            !executable.NodesById.TryGetValue(state.Execution.ExecutableNodeId, out var node) ||
            node.DescriptorType != SpecialistRuntimeIds.ConsumerKey || node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) return null;

        SpecialistRuntimeDescriptor descriptor;
        SpecialistInvocationIdentity identity;
        try
        {
            descriptor = SpecialistNativeBinding.Descriptor(node);
            identity = SpecialistNativeBinding.CancellationIdentity(workflow, state, node);
        }
        catch (SpecialistTurnException) { return null; }

        return new(workflow.WorkflowExecutionId, state.InvocationId,
            new(descriptor.DefinitionId!, descriptor.DefinitionVersionId!, descriptor.Version!, descriptor.TemplateHash!,
                identity.ArtifactId, identity.ArtifactHash, descriptor.AllowContinuity),
            descriptor.Model, descriptor.Limits, state.Status.ToString(), Fault(state), Output(workflow, state, node),
            await TurnAsync(state, node, identity, cancellationToken));
    }

    private async ValueTask<SpecialistTurnEvidence?> TurnAsync(ActivityExecutionState state, ExecutableNode node, SpecialistInvocationIdentity identity, CancellationToken cancellationToken)
    {
        if (await HandleAsync(state, node, cancellationToken) is not { } handle) return null;
        var turn = await turns.ReadAsync(handle, identity, cancellationToken);
        if (turn is null) return null;
        SpecialistConversationEvidence? conversation = null;
        if (turn.Conversation is { } admission)
        {
            var current = await turns.ReadConversationAsync(admission.ConversationId, identity, cancellationToken);
            conversation = new(admission.ConversationId, admission.ExpectedRevision, current?.Revision, current?.OutcomeUnknown ?? false, current?.ActiveTurnHandle is not null);
        }
        var requests = (turn.ModelRequests ?? []).Select(x => new SpecialistModelRequestEvidence(x.Id, x.ReservedAt, x.MaximumOutputTokens,
            x.Observation.ProviderKey, x.Observation.ConfiguredModelId)).ToArray();
        var usage = turn.ResponseObservation is { } observed
            ? new SpecialistUsageEvidence(observed.ObservedAt, observed.UsageAvailable, observed.InputTokens, observed.OutputTokens, observed.TotalTokens, observed.ObservedModelId)
            : null;
        return new(turn.Status, turn.FailureCode, Application(turn, state, identity), turn.Deadline, turn.MaximumModelCalls, requests, usage, conversation, turn.ContentPurgedAt);
    }

    // A running boundary holds its staged handle as private state; completion releases it, and the model-turn child keeps
    // the same handle in its verified scheduling provenance. Neither one means the turn was never staged: still scheduled,
    // or refused at entry with a native fault. Disagreeing children read as no turn.
    private async ValueTask<string?> HandleAsync(ActivityExecutionState state, ExecutableNode node, CancellationToken cancellationToken)
    {
        try
        {
            if (state.PrivateState is not null) return SpecialistNativeBinding.Handle(state);
            var worker = SpecialistNativeBinding.ChildNode(node).ExecutableNodeId;
            var handles = new HashSet<string?>(StringComparer.Ordinal);
            string? continuation = null;
            do
            {
                var page = await activities.ListByParentPageAsync(new(state.Execution.WorkflowExecutionId, state.InvocationId, continuationToken: continuation), cancellationToken);
                foreach (var child in page.Items.Where(x => x.Execution.ExecutableNodeId == worker))
                    handles.Add(child.Provenance.Metadata.GetValueOrDefault(SpecialistNativeBinding.TurnHandleMetadataKey));
                continuation = page.NextContinuationToken;
            } while (continuation is not null);
            return handles.Count == 1 && handles.Single() is { Length: 64 } handle ? handle : null;
        }
        catch (SpecialistTurnException) { return null; }
    }

    private static SpecialistInvocationFaultEvidence? Fault(ActivityExecutionState state) => state.Fault is { } fault
        ? new(fault.Code, fault.Code.StartsWith("specialist.", StringComparison.Ordinal) ? fault.Message : null)
        : null;

    // One turn serves every native delivery of its logical operation, so only an application bound to this invocation's
    // own identity counts. A natively completed boundary has consumed the accepted result before its settlement is recorded.
    private static SpecialistTurnApplicationStatus Application(SpecialistTurnDocument turn, ActivityExecutionState state, SpecialistInvocationIdentity identity)
    {
        if (turn.Status != SpecialistTurnStatus.Accepted) return SpecialistTurnApplicationStatus.NotAccepted;
        if (turn.Applications?.Any(x => x.Identity == identity) == true) return SpecialistTurnApplicationStatus.Applied;
        return state.Status switch
        {
            ActivityExecutionStatus.Completed => SpecialistTurnApplicationStatus.SettlementPending,
            ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded => SpecialistTurnApplicationStatus.NotApplied,
            _ => SpecialistTurnApplicationStatus.Pending
        };
    }

    // The same rule generic runtime inspection applies: a value hidden by the envelope, the contract result or any projected
    // member withholds the whole result, and the host's payload capture policy decides the rest. Only a full payload capture
    // returns the value here; a diagnostic snapshot stays with the runtime's own inspection.
    private SpecialistInvocationOutputEvidence? Output(WorkflowExecutionState workflow, ActivityExecutionState state, ExecutableNode node)
    {
        if (state.Completion?.Result is not { Presence: ValuePresence.Present } result) return null;
        var contract = node.ActivityContract!.Result;
        var sensitive = result.Policy.HidesValue() || contract.Policy.HidesValue() || contract.Projections.Values.Any(x => x.Policy.HidesValue());
        var decision = capture.Decide(new(RuntimePayloadCaptureSubject.ActivityOutput, workflow.WorkflowExecutionId, time.GetUtcNow(),
            activityExecutionId: state.InvocationId, valueName: OutputName, type: new RuntimeValueTypeDescriptor("alias", result.Type.Alias, result.Type.Schema),
            isSensitive: sensitive));
        return new(decision.Mode.ToString(), !sensitive && decision.CapturesPayload && result.InlineValue is { } value ? value.Clone() : null);
    }

    private const string OutputName = "Result";
}
