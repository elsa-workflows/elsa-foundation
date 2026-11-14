using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Native process-private selected execution. Shared-storage interpreters remain unqualified.</summary>
internal sealed class QueuedSelectedStart(IServiceProvider services, WorkflowTestEntryAdmission admission,
    RuntimeTestContextPreparer preparer, TimeProvider clock)
{
    internal async ValueTask<RuntimeTestContextPreparation> PrepareAsync(WorkflowExecutionStartCommandPayload payload,
        WorkflowExecutable executable, string executionId, IWorkflowExecutionStateStore executions,
        IWorkflowSchedulerWorkQueue? scheduler, RuntimeCheckpointCommitter? committer, IWorkflowExecutionActorProvider? callerActors, CancellationToken cancellationToken)
    {
        var entry = payload.TestEntry ?? throw Refused("The selected start has no trusted entry.");
        RequireProcessPrivate(executions, scheduler, committer, callerActors);
        if (services.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable) is not (PersistenceMode.Durable or PersistenceMode.Balanced))
            throw Refused("The selected interpreter has no qualified persistence-mode profile.");
        await admission.ValidateAsync(entry, executable, payload.RunKind, payload.TestScope, payload.Authority,
            payload.TenantId, payload.Partition, payload.ParentWorkflowExecutionId, payload.TriggerNodeId,
            payload.StartAuthority, payload.DispatchNestingDepth, payload.StimulusInput is not null || payload.TriggerMetadata.Count != 0,
            cancellationToken);
        await RequireSourceAsync(payload, cancellationToken);
        if (!executable.NodesById.TryGetValue(entry.ExecutableNodeId, out var target))
            throw Refused("The exact selected target is unavailable in the pinned executable.");
        var profiles = services.GetServices<IRuntimeSelectedStartTargetProfile>().ToArray();
        if (profiles.Length != 1) throw Refused("Selected work requires exactly one model/effect owner.");
        var pipeline = services.GetRequiredService<IRuntimeActivityExecutionPipeline>();
        var ownedNodes = OwnedNodes(target).ToArray();
        if (target.ChildSlots.Count == 0)
        {
            if (!await profiles[0].CanExecuteAsync(entry, executable, target, pipeline, cancellationToken))
                throw Refused("The exact selected leaf and its complete effect profile are unqualified.");
        }
        else
            foreach (var node in ownedNodes)
                if (!await profiles[0].CanExecuteOwnedNodeAsync(entry, executable, target, node, pipeline, cancellationToken))
                    throw Refused("Every node of selected owned work requires explicit model/effect qualification.");
        var prepared = await preparer.PrepareAsync(entry, executable, executionId, payload.Inputs, payload.Variables, cancellationToken);
        if (!prepared.IsAvailable || !prepared.Context!.OwnedNodeIds.SequenceEqual(ownedNodes.Select(x => x.ExecutableNodeId), StringComparer.Ordinal))
            throw Refused("The trusted selected setup is unavailable.");
        // Match RuntimeContainerScopeService.BuildVisibleFramesCoreAsync: a scope's iteration is outer
        // to its own container. Supplied frames must preserve the same lexical shadowing order.
        if (prepared.Context!.Frames.GroupBy(x => x.Frame.ScopeId, StringComparer.Ordinal).Any(group =>
            group.Count() > 1 && !group.Select(x => x.Frame.Kind).SequenceEqual([VariableFrameKind.Iteration, VariableFrameKind.Container])))
            throw Refused("Supplied frames must retain the native iteration-before-container order for each scope.");
        if (target.OutputCaptures.Values.Where(x => x.CaptureOnSuccessfulCompletion && x.Metadata.ContainsKey(RuntimeMetadataKeys.CaptureTargetScopeId))
            .GroupBy(x => (x.Metadata[RuntimeMetadataKeys.CaptureTargetScopeId],
                x.Metadata.GetValueOrDefault(RuntimeMetadataKeys.TargetVariableReferenceKey))).Any(x => x.Count() != 1))
            throw Refused("Competing selected captures into one supplied variable are unavailable.");
        await RequireSourceAsync(payload, cancellationToken);
        return prepared;
    }

    private static IEnumerable<ExecutableNode> OwnedNodes(ExecutableNode node)
    {
        yield return node;
        foreach (var child in node.ChildSlots.SelectMany(x => x.Activities))
            foreach (var owned in OwnedNodes(child)) yield return owned;
    }

    private async ValueTask RequireSourceAsync(WorkflowExecutionStartCommandPayload payload, CancellationToken cancellationToken)
    {
        var source = payload.PinnedSource is { } provenance
            ? await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(provenance.SourceReferenceId, cancellationToken) : null;
        if (source is null || source.Scope != WorkflowExecutableReferenceScope.TestRun || source.ExpiresAt is null ||
            !source.IsLive(clock.GetUtcNow()) || source.ArtifactId != payload.PinnedExecutable.ArtifactId ||
            WorkflowExecutableSourceProvenance.From(source) != payload.PinnedSource)
            throw Refused("A selected start requires its exact finite live TestRun source.");
    }

    private void RequireProcessPrivate(IWorkflowExecutionStateStore executions, IWorkflowSchedulerWorkQueue? scheduler,
        RuntimeCheckpointCommitter? committer, IWorkflowExecutionActorProvider? callerActors)
    {
        var selectedExecutions = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var selectedQueue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var selectedCommitter = services.GetRequiredService<RuntimeCheckpointCommitter>();
        var checkpoint = Physical(services.GetRequiredService<IRuntimeCheckpointCommitStore>());
        var scopeOwners = services.GetServices<IWorkflowTestScopeStore>().ToArray();
        var frameOwners = services.GetServices<IRuntimeTestContextFrameAccess>().ToArray();
        if (frameOwners.Length != 1 || !ReferenceEquals(frameOwners[0], services.GetRequiredService<RuntimeTestContextOwner>()) ||
            !services.GetRequiredService<RuntimeTestContextOwner>().Uses(selectedExecutions, services.GetRequiredService<IActivityExecutionStateStore>()) ||
            callerActors is not null && !ReferenceEquals(callerActors, services.GetRequiredService<IWorkflowExecutionActorProvider>()) ||
            !ReferenceEquals(executions, selectedExecutions) || scheduler is not null && !ReferenceEquals(scheduler, selectedQueue) ||
            committer is not null && !ReferenceEquals(committer, selectedCommitter) ||
            !ReferenceEquals(Physical(selectedCommitter.SelectedStore), checkpoint) ||
            Physical(selectedExecutions) is not InMemoryWorkflowExecutionStateStore memory ||
            checkpoint is not InMemoryRuntimeCheckpointCommitStore commits ||
            !ReferenceEquals(Physical(services.GetRequiredService<IRuntimePostCommitOutboxStore>()), commits) ||
            Physical(selectedQueue) is not InMemoryWorkflowSchedulerWorkQueue ||
            services.GetRequiredService<IBackgroundActivityWorkStore>() is not InMemoryBackgroundActivityWorkStore background ||
            services.GetRequiredService<IDurableTimerStore>() is not InMemoryDurableTimerStore ||
            scopeOwners.Length != 1 || scopeOwners[0] is not InMemoryWorkflowTestScopeStore scopes ||
            !commits.CoordinatesSelectedStart(memory, scopes, background) ||
            services.GetRequiredService<IWorkflowExecutionActorProvider>() is not InProcessWorkflowExecutionActorProvider actors ||
            !actors.UsesSelectedInterpreter(services.GetRequiredService<IWorkflowExecutionCommandExecutor>(),
                services.GetRequiredService<IPersistenceOperationScopeFactory>()))
            throw Refused("Selected starts require one coherent process-private native interpreter; shared providers are unqualified.");
    }

    private static object Physical(object store) => store switch
    {
        CoalescingWorkflowExecutionStateStore wrapper => wrapper.PhysicalStore,
        CoalescingWorkflowSchedulerWorkQueue wrapper => wrapper.PhysicalStore,
        CoalescingRuntimeCheckpointCommitStore wrapper => wrapper.PhysicalStore,
        CoalescingRuntimePostCommitOutboxStore wrapper => wrapper.PhysicalStore,
        _ => store
    };

    internal bool IsCheckpointReplay(RuntimeSchedulerWorkItem work, RuntimeCheckpointCommandPayload payload,
        IWorkflowExecutionStateStore executions, RuntimeCheckpointCommitter committer)
    {
        RequireProcessPrivate(executions, null, committer, null);
        return ((InMemoryRuntimeCheckpointCommitStore)Physical(committer.SelectedStore)).IsSelectedStartReplay(work, payload);
    }

    internal const string PayloadReceiptKey = "runtime.selectedStartPayloadHash";

    internal static string PayloadHash(RuntimeSchedulerWorkItem work) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(work.Payload!.Value.GetRawText())));

    private const string ProtocolProperty = nameof(WorkflowExecutionStartCommandPayload.SelectedStartProtocol);

    internal static bool HasWireProtocol(JsonElement? payload) => payload is { ValueKind: JsonValueKind.Object } value &&
        value.EnumerateObject().Any(x => x.Name.Equals(ProtocolProperty, StringComparison.OrdinalIgnoreCase) &&
            x.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));

    internal static void ValidateWireEntry(JsonElement? payload, WorkflowTestEntry? entry)
    {
        if (entry is not null) RequireWireProtocol(payload);
        else if (HasWireProtocol(payload)) throw Refused("A selected interpreter marker requires its exact trusted entry.");
    }

    internal static void RequireWireProtocol(JsonElement? payload)
    {
        var protocol = payload is { ValueKind: JsonValueKind.Object } value
            ? value.EnumerateObject().FirstOrDefault(x => x.Name.Equals(ProtocolProperty, StringComparison.OrdinalIgnoreCase)).Value : default;
        if (protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var version) || version != 1)
            throw Refused("The queued selected start has no supported process-private interpreter protocol.");
    }

    internal static Exception Refused(string message) => WorkflowTestEntryAdmission.Refused("test-entry-execution-unavailable", message);
}
