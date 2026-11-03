using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

internal sealed class SpecialistNativeBinding(IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables, IPersistenceAccessContextAccessor access)
{
    public const string TurnHandleMetadataKey = "specialist.turn";
    public async ValueTask<SpecialistInvocationIdentity> IdentifyAsync(IRuntimeActivityExecutionContext context, CancellationToken cancellationToken)
    {
        var workflow = await WorkflowAsync(context.WorkflowExecutionId, cancellationToken);
        if (workflow.PinnedExecutable != context.PinnedExecutable || context.ActivityExecutionState.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId ||
            context.ActivityExecutionState.Execution.ExecutableNodeId != context.ExecutableNode.ExecutableNodeId)
            throw Failure();
        return Identity(workflow, context.ActivityExecutionState, context.ExecutableNode);
    }

    public async ValueTask<(SpecialistInvocationIdentity Identity, ActivityExecutionState Parent, ExecutableNode Node)> ParentAsync(ActivityExecutionContext child, CancellationToken cancellationToken)
    {
        var workflow = await WorkflowAsync(child.WorkflowExecutionId, cancellationToken);
        var state = await activities.FindAsync(child.WorkflowExecutionId, child.InvocationId, cancellationToken) ?? throw Failure();
        if (state.Execution.ExecutableNodeId != child.ExecutableNodeId || state.ParentActivityExecutionId is not { } parentId ||
            state.Status != ActivityExecutionStatus.Running) throw Failure();
        var parent = await activities.FindAsync(child.WorkflowExecutionId, parentId, cancellationToken) ?? throw Failure();
        var executable = await executables.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken) ?? throw Failure();
        if (executable.Identity.ArtifactHash != workflow.PinnedExecutable.ArtifactHash || parent.Status != ActivityExecutionStatus.Running ||
            !executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node) ||
            !executable.NodesById.TryGetValue(child.ExecutableNodeId, out var worker) ||
            worker.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.TurnCapability || worker.ExecutionPlacement != Primitives.Models.ActivityExecutionPlacement.Background ||
            ChildNode(node).ExecutableNodeId != worker.ExecutableNodeId ||
            state.Provenance.Metadata.GetValueOrDefault(TurnHandleMetadataKey) != Handle(parent)) throw Failure();
        return (Identity(workflow, parent, node), parent, node);
    }

    public async ValueTask VerifyChildCompletionAsync(IRuntimeActivityExecutionContext parent, string childInvocationId, string childNodeId, string handle, CancellationToken cancellationToken)
    {
        var child = await activities.FindAsync(parent.WorkflowExecutionId, childInvocationId, cancellationToken) ?? throw Failure();
        if (child.ParentActivityExecutionId != parent.ActivityExecutionState.InvocationId || child.Execution.ExecutableNodeId != childNodeId ||
            childNodeId != ChildNode(parent.ExecutableNode).ExecutableNodeId || child.Status != ActivityExecutionStatus.Completed ||
            child.Completion?.Result.InlineValue is not { ValueKind: JsonValueKind.String } receipt || receipt.GetString() != handle ||
            child.Provenance.Metadata.GetValueOrDefault(TurnHandleMetadataKey) != handle)
            throw Failure();
    }

    internal static void VerifyRequest(SpecialistTurnRequest request, SpecialistInvocationIdentity identity, ActivityExecutionState parent, ExecutableNode node)
    {
        if (request.Identity != identity || request.Contract.SchemaFingerprint != node.ActivityContract!.SchemaFingerprint ||
            request.BindingFingerprint != parent.InputSnapshot!.BindingFingerprint) throw Failure();
    }

    public static SpecialistRuntimeDescriptor Descriptor(ExecutableNode node)
    {
        SpecialistRuntimeDescriptor? descriptor;
        try { descriptor = node.DescriptorPayload.Deserialize<SpecialistRuntimeDescriptor>(SpecialistJson.Options); }
        catch (JsonException exception) { throw Failure(exception); }
        if (descriptor is null || node.DescriptorType != SpecialistRuntimeIds.ConsumerKey || node.DescriptorSchemaVersion is not (SpecialistRuntimeIds.DescriptorSchema or SpecialistRuntimeIds.ContinuityDescriptorSchema) ||
            (node.DescriptorSchemaVersion == SpecialistRuntimeIds.ContinuityDescriptorSchema) != SpecialistNativeControls.Enabled(descriptor) ||
            node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability ||
            node.ActivityContract.DescriptorKind != SpecialistRuntimeIds.ConsumerKey ||
            !JsonElement.DeepEquals(node.ActivityContract.DescriptorPayload, node.DescriptorPayload) ||
            !Stamped(node, "definitionId", descriptor.DefinitionId) || !Stamped(node, "definitionVersionId", descriptor.DefinitionVersionId) ||
            !Stamped(node, "version", descriptor.Version) || !Stamped(node, "templateHash", descriptor.TemplateHash) || !Stamped(node, "sourceReferenceId", descriptor.SourceReferenceId))
            throw Failure();
        return descriptor;
    }

    public static string Handle(ActivityExecutionState state) =>
        state.PrivateState is { StateVersion: 1, Value.InlineValue: { ValueKind: JsonValueKind.String } value } && value.GetString() is { Length: 64 } handle
            ? handle : throw SpecialistModelRunner.Failure("specialist.journal.handle", "The native specialist boundary has no valid staged turn handle.");

    public static ExecutableNode ChildNode(ExecutableNode node) => node.ChildSlots.Count == 1 && node.ChildSlots.Single() is { Name: "model-turn", Activities.Count: 1 } slot
        ? slot.Activities.Single() : throw Failure();

    private async ValueTask<WorkflowExecutionState> WorkflowAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var scope = access.Current.RequireScope();
        var workflow = await workflows.FindAsync(workflowExecutionId, cancellationToken) ?? throw Failure();
        if (workflow.Status != WorkflowExecutionStatus.Running || workflow.Partition?.Value != scope.Value) throw Failure();
        access.Current.EnsureTenantScope(workflow.TenantId);
        return workflow;
    }

    internal static SpecialistInvocationIdentity Identity(WorkflowExecutionState workflow, ActivityExecutionState state, ExecutableNode node)
    {
        var descriptor = Descriptor(node);
        foreach (var capture in node.OutputCaptures.Values.Where(x => x.CaptureOnSuccessfulCompletion))
        {
            // Graph-owned captures have no stronger destination policy in this profile. Root variable
            // destinations carry their policy in the canonical committed frame, including absent values.
            if (capture.Metadata.ContainsKey(RuntimeMetadataKeys.CaptureTargetScopeId) ||
                !capture.Metadata.TryGetValue(RuntimeMetadataKeys.TargetVariableReferenceKey, out var variableKey)) continue;
            if (workflow.RootVariableFrame is null || !workflow.RootVariableFrame.Values.TryGetValue(variableKey, out var destination))
                throw SpecialistModelRunner.Failure("specialist.capture.target", "The specialist capture has no declared destination in this native workflow.");
            if (destination.Policy.RequiresEncryption || destination.Presence == ValuePresence.Withheld)
                throw SpecialistModelRunner.Failure("specialist.value.encryption", "This specialist profile cannot dispatch into encryption-required or withheld capture destinations.");
        }
        if (state.ContractIdentity?.SchemaFingerprint != node.ActivityContract!.SchemaFingerprint || state.InputSnapshot is null ||
            state.InputSnapshot.ContractFingerprint != node.ActivityContract.SchemaFingerprint || state.InputSnapshot.InvocationId != state.InvocationId)
            throw Failure();
        return new(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, state.InvocationId, state.OperationKey,
            workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
    }

    internal static SpecialistInvocationIdentity CancellationIdentity(WorkflowExecutionState workflow, ActivityExecutionState state, ExecutableNode node)
    {
        var descriptor = Descriptor(node);
        if (state.Execution.WorkflowExecutionId != workflow.WorkflowExecutionId || state.Execution.ExecutableNodeId != node.ExecutableNodeId ||
            state.ContractIdentity is { } contract && contract.SchemaFingerprint != node.ActivityContract!.SchemaFingerprint ||
            state.InputSnapshot is { } input && (input.InvocationId != state.InvocationId || input.ContractFingerprint != node.ActivityContract!.SchemaFingerprint))
            throw Failure();
        // Cancellation also covers a scheduled invocation whose input snapshot/staged handle does
        // not exist yet. It grants no permission to infer, and never weakens normal entry/worker checks.
        return new(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, state.InvocationId, state.OperationKey,
            workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
    }

    private static bool Stamped(ExecutableNode node, string key, string? value) =>
        !string.IsNullOrWhiteSpace(value) && node.Metadata.TryGetValue("activity." + key, out var actual) && actual == value;
    private static SpecialistTurnException Failure(Exception? innerException = null) => SpecialistModelRunner.Failure("specialist.native.binding", "The specialist request does not match the committed native execution and artifact.", innerException);
}
