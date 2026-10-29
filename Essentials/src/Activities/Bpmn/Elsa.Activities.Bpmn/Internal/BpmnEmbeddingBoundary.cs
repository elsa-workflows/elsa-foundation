using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnEmbeddingBoundary(BpmnNativeWorkBoundary native, IWorkflowExecutableStore materials,
    IBookmarkStateStore bookmarks, IRuntimeCheckpointCommitStore checkpoints,
    IPersistenceAccessContextAccessor access, TimeProvider clock, BpmnModelHostingOptions options,
    CoalescingInner<IBookmarkStateStore>? physicalBookmarks = null, IRuntimeModelExecutionReader? models = null)
{
    internal const string PinKey = "bpmn.model.v1";
    internal const string ResumeKey = "bpmn-model-completed";
    internal const string StimulusType = "elsa.bpmn.model-completed.v1";
    internal const string ProviderId = "elsa.bpmn.native-parent.v1";
    internal const string StartKind = "Elsa.Bpmn.StartEmbedded.v1";
    internal const string CancelKind = "Elsa.Bpmn.CancelEmbedded.v1";
    internal const string ResultKind = "Elsa.Bpmn.ResumeNativeParent.v1";
    internal static readonly ValueTypeDescriptor CompletionType = new(typeof(RunBpmnCompletion).FullName!, schemaVersion: 1);
    internal IBookmarkStateStore PhysicalBookmarks => physicalBookmarks?.Value ?? bookmarks;

    internal void EnsureQualified()
    {
        native.EnsureQualified();
        if (!checkpoints.SupportsModelParentAdmission || !checkpoints.CanCommitExactBookmarkResume(PhysicalBookmarks, native.PhysicalWorkflows, native.PhysicalActivities))
            throw new NotSupportedException("RunBpmn requires the qualified physical native parent wait and model admission writer.");
    }

    internal async ValueTask<BpmnEmbeddedInvocation> PrepareAsync(ActivityExecutionContext context, string selection)
    {
        EnsureQualified();
        if (!options.AdmitNewExecutions) throw new NotSupportedException("New RunBpmn model invocation admission is disabled.");
        var workflow = await native.PhysicalWorkflows.FindAsync(context.WorkflowExecutionId, context.CancellationToken) ?? throw BpmnEmbeddingJson.Invalid();
        access.Current.EnsureTenantScope(workflow.TenantId);
        var scope = access.Current.RequireScope();
        var executable = await materials.FindAsync(workflow.PinnedExecutable.ArtifactId, context.CancellationToken) ?? throw BpmnEmbeddingJson.Invalid();
        if (workflow.Partition?.Value != scope.Value || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, workflow.PinnedExecutable) ||
            !executable.NodesById.TryGetValue(context.ExecutableNodeId, out var node) || node.ActivityType != RunBpmn.ActivityType ||
            !node.Metadata.TryGetValue(PinKey, out var pinJson) || node.InputBindings.GetValueOrDefault(nameof(RunBpmn.Model)) is not
                { Source: RuntimeInputBindingSource.Literal, LiteralValue: { ValueKind: JsonValueKind.String } literal } || literal.GetString() != selection)
            throw BpmnEmbeddingJson.Invalid();
        var pin = BpmnEmbeddingJson.Read<BpmnEmbeddingPin>(pinJson);
        if (pin.SourceReferenceId != selection || !executable.Dependencies.Any(edge => edge.ArtifactId == pin.Executable.ArtifactId && edge.ArtifactHash == pin.Executable.ArtifactHash))
            throw BpmnEmbeddingJson.Invalid();
        await RequireMaterialAsync(pin, context.CancellationToken);
        var target = WorkflowExecutableResumeTarget.FindForNode(executable, node.ExecutableNodeId, ResumeKey) ?? throw BpmnEmbeddingJson.Invalid();
        if (target.HandlerKey != "ResumeAsync" || target.ExecutableNodeId != node.ExecutableNodeId) throw BpmnEmbeddingJson.Invalid();
        return new(1, scope, "bpmn-embedded:" + BpmnModelIdentity.Create(scope.Value, context.WorkflowExecutionId, context.InvocationId), pin,
            workflow.PinnedExecutable, context.WorkflowExecutionId, context.InvocationId, context.ExecutableNodeId, target.ResumeTargetId, null);
    }

    internal async ValueTask RequireMaterialAsync(BpmnEmbeddingPin pin, CancellationToken token)
    {
        var material = await materials.FindMaterialAsync(pin.Executable.ArtifactId, token);
        if (material?.Model is not { Profile: BpmnModelProtocol.Profile } model || model.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) || material.IncarnationId != pin.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, pin.Executable)) throw BpmnEmbeddingJson.Invalid();
    }

    internal static BpmnEmbeddedInvocation Read(RunBpmnState state)
    {
        if (state.FormatVersion != 1) throw BpmnEmbeddingJson.Invalid();
        var invocation = BpmnEmbeddingJson.Read<BpmnEmbeddedInvocation>(state.Invocation);
        if (invocation.FormatVersion != 1 || invocation.Model is null || invocation.Scope is null ||
            invocation.ExecutionId != "bpmn-embedded:" + BpmnModelIdentity.Create(invocation.Scope.Value, invocation.NativeExecutionId, invocation.NativeInvocationId))
            throw BpmnEmbeddingJson.Invalid();
        return invocation;
    }

    internal static BpmnEmbeddedInvocation Read(ActivityExecutionState activity)
    {
        if (activity.Execution.ActivityType != RunBpmn.ActivityType || activity.PrivateState is not { StateVersion: 1 } state ||
            state.InvocationId != activity.InvocationId || state.Value.Type != new ValueTypeDescriptor(typeof(RunBpmnState).FullName!, schemaVersion: 1) ||
            state.Value.InlineValue is not { } value)
            throw BpmnEmbeddingJson.Invalid();
        var invocation = Read(value.Deserialize<RunBpmnState>() ?? throw BpmnEmbeddingJson.Invalid());
        if (invocation.NativeExecutionId != activity.Execution.WorkflowExecutionId || invocation.NativeInvocationId != activity.InvocationId ||
            invocation.NativeNodeId != activity.Execution.ExecutableNodeId) throw BpmnEmbeddingJson.Invalid();
        return invocation;
    }

    internal async ValueTask<RuntimeModelExecutionState> RequireModelAsync(BpmnEmbeddedInvocation invocation, CancellationToken token)
    {
        EnsureQualified(); access.Current.EnsureScope(invocation.Scope);
        var model = await (models ?? throw BpmnNativeWorkBoundary.Unavailable("The model reader is unavailable.")).FindAsync(invocation.ExecutionId, token)
            ?? throw BpmnNativeWorkBoundary.Unavailable("The embedded model is unavailable.");
        access.Current.EnsureTenantScope(model.TenantId);
        if (model.MaterialIncarnationId != invocation.Model.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(model.Executable, invocation.Model.Executable) ||
            BpmnDirectExecutionDriver.Read(model).NativeParent is not { } parent || !JsonElement.DeepEquals(parent.Invocation, BpmnEmbeddingJson.Write(invocation)))
            throw BpmnEmbeddingJson.Invalid();
        return model;
    }

    internal async ValueTask<(WorkflowExecutionState Workflow, ActivityExecutionState Activity)> ParentAsync(BpmnEmbeddedInvocation invocation, CancellationToken token)
    {
        EnsureQualified(); access.Current.EnsureScope(invocation.Scope);
        var workflow = await native.PhysicalWorkflows.FindAsync(invocation.NativeExecutionId, token) ?? throw BpmnNativeWorkBoundary.Unavailable("The native parent is unavailable.");
        var activity = await native.PhysicalActivities.FindAsync(invocation.NativeExecutionId, invocation.NativeInvocationId, token) ?? throw BpmnNativeWorkBoundary.Unavailable("The native RunBpmn invocation is unavailable.");
        access.Current.EnsureTenantScope(workflow.TenantId);
        if (workflow.Partition?.Value != invocation.Scope.Value || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, invocation.NativeExecutable) ||
            activity.Execution.ActivityType != RunBpmn.ActivityType || activity.Execution.ExecutableNodeId != invocation.NativeNodeId) throw BpmnEmbeddingJson.Invalid();
        if (activity.PrivateState is not null)
        {
            if (!JsonElement.DeepEquals(BpmnEmbeddingJson.Write(Read(activity)), BpmnEmbeddingJson.Write(invocation))) throw BpmnEmbeddingJson.Invalid();
        }
        else
        {
            // Native completion/cancellation legitimately retires private state. Verify the immutable native
            // node/pin and actual terminal responsibility instead of requiring that discarded value.
            if (!workflow.Status.IsTerminal() && activity.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled))
                throw BpmnNativeWorkBoundary.Unavailable("The native parent invocation has no retained live wait or terminal responsibility.");
            var executable = await materials.FindAsync(invocation.NativeExecutable.ArtifactId, token) ?? throw BpmnNativeWorkBoundary.Unavailable("The native parent material is unavailable.");
            if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, invocation.NativeExecutable) ||
                !executable.NodesById.TryGetValue(invocation.NativeNodeId, out var node) || node.ActivityType != RunBpmn.ActivityType ||
                !node.Metadata.TryGetValue(PinKey, out var pin) || BpmnEmbeddingJson.Read<BpmnEmbeddingPin>(pin) != invocation.Model)
                throw BpmnEmbeddingJson.Invalid();
        }
        return (workflow, activity);
    }

    internal async ValueTask<StimulusResumeRecipient?> ObserveParentAsync(BpmnEmbeddedInvocation invocation, CancellationToken token)
    {
        var (workflow, activity) = await ParentAsync(invocation, token);
        if (workflow.Status.IsTerminal() || activity.Status is ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Faulted) return null;
        var observation = await PhysicalBookmarks.ObserveAsync(invocation.NativeExecutionId, invocation.RegistrationId!, token)
            ?? throw BpmnNativeWorkBoundary.Unavailable("The exact RunBpmn wait is unavailable.");
        EnsureBookmark(invocation, observation.State);
        var expected = StimulusResumeRecipient.From(observation, workflow, invocation.Scope);
        if (!expected.MatchesAdmission(observation, workflow, activity, invocation.Scope, clock.GetUtcNow()))
            throw BpmnNativeWorkBoundary.Unavailable("The native parent wait changed before model admission.");
        return expected;
    }

    internal static void EnsureBookmark(BpmnEmbeddedInvocation invocation, BookmarkState bookmark)
    {
        if (bookmark.WorkflowExecutionId != invocation.NativeExecutionId || bookmark.ActivityExecutionId != invocation.NativeInvocationId ||
            bookmark.ExecutableNodeId != invocation.NativeNodeId || bookmark.BookmarkId != invocation.RegistrationId ||
            bookmark.ResumeTargetId != invocation.ResumeTargetId || bookmark.StimulusType != StimulusType || bookmark.StimulusHash != invocation.ExecutionId)
            throw BpmnEmbeddingJson.Invalid();
    }

    internal async ValueTask<(BpmnEmbeddedInvocation Invocation, StimulusResumeRecipient? Wait)> ValidateInputAsync(RuntimeModelExecutionContext context, CancellationToken token)
    {
        var input = context.Command.Payload.Deserialize<BpmnEmbeddedInput>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw BpmnEmbeddingJson.Invalid();
        var invocation = Read(new RunBpmnState(1, input.Invocation));
        if (invocation.RegistrationId is null || invocation.NativeAttemptId is null || context.Scope != invocation.Scope ||
            context.WorkItem.WorkflowExecutionId != invocation.ExecutionId || context.Command.MaterialIncarnationId != invocation.Model.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(context.Command.Executable, invocation.Model.Executable) ||
            context.Command.InputId != context.Command.InputKind + ":" + BpmnModelIdentity.Create(input.SourceOutboxItemId)) throw BpmnEmbeddingJson.Invalid();
        var source = await native.PhysicalLookup.FindDurableAsync(input.SourceOutboxItemId, token) ?? throw BpmnNativeWorkBoundary.Unavailable("The committed RunBpmn source is unavailable.");
        if (source.StimulusDeliverySourceScope != context.Scope || source.Intent.Payload is not { } payload) throw BpmnEmbeddingJson.Invalid();
        if (source.Status == RuntimePostCommitOutboxStatus.FailedFinal || source.Status == RuntimePostCommitOutboxStatus.Delivered && context.State is null)
            throw BpmnNativeWorkBoundary.Unavailable("The RunBpmn source no longer owes initial admission.");
        var (workflow, activity) = await ParentAsync(invocation, token);
        if (workflow.TenantId != context.Command.TenantId || workflow.Authority is not { } authority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(authority, context.Command.Authority.SystemIdentity, context.Command.Authority.RootInitiator, context.Command.Authority.Metadata))
            throw BpmnEmbeddingJson.Invalid();
        if (context.State is { } state)
            await RequireModelAsync(invocation, token);
        if (context.Command.InputKind == "native-parent-settled")
        {
            var completion = payload.Deserialize<RunBpmnCompletion>() ?? throw BpmnEmbeddingJson.Invalid();
            if (source.Intent.Kind != ResultKind || source.Intent.WorkflowExecutionId != invocation.ExecutionId || context.State?.TerminalResult is not { } terminal ||
                !JsonElement.DeepEquals(completion.Invocation, input.Invocation) || !JsonElement.DeepEquals(completion.Result, terminal) ||
                !await ParentProofAsync(invocation, token)) throw BpmnEmbeddingJson.Invalid();
            return (invocation, null);
        }
        var cancelled = context.Command.InputKind == "cancel-embedded";
        if (source.Intent.Kind != (cancelled ? CancelKind : StartKind) || source.Intent.WorkflowExecutionId != invocation.NativeExecutionId ||
            !JsonElement.DeepEquals(payload, input.Invocation)) throw BpmnEmbeddingJson.Invalid();
        if (cancelled)
        {
            if (!workflow.Status.IsTerminal() && activity.Status != ActivityExecutionStatus.Cancelled) throw BpmnEmbeddingJson.Invalid();
            return (invocation, null);
        }
        return (invocation, context.State is null ? await ObserveParentAsync(invocation, token) : null);
    }

    internal async ValueTask<bool> ParentProofAsync(BpmnEmbeddedInvocation invocation, CancellationToken token)
    {
        var (workflow, activity) = await ParentAsync(invocation, token);
        if (workflow.Status.IsTerminal() || activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled) return true;
        return activity.TriggerDeliveries?.Any(receipt => receipt.Status == ActivityTriggerDeliveryStatus.Consumed &&
            receipt.RegistrationId == invocation.RegistrationId && receipt.ProviderId == ProviderId && receipt.PayloadType == CompletionType) == true;
    }
}
