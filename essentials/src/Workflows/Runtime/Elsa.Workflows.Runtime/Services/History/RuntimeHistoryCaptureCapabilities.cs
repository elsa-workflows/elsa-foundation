using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Checks actual selected write-input support lazily, without reading diagnostic content or activating an actor.</summary>
internal sealed class RuntimeHistoryCaptureCapabilities(IServiceProvider services)
{
    internal bool SourceInputs() => services.GetService<IRuntimeHistorySourceExtension>() is { } selected &&
        ReferenceEquals(selected, services.GetService<RuntimeCheckpointCommitter>()?.HistorySourceResolver?.Invoke());
    internal bool CleanupInputs() => services.GetService<IActivityScopeCleanupStore>()?.SupportsHistoryBookmarks == true;
    internal bool Values() => services.GetService<IRuntimeActivityExecutionInspectionAccumulator>()?.SupportsCheckpointValueCapture == true;
    internal bool DeliveryInputs() => services.GetService<IRuntimeHistoryObservationWriter>() is { } writer &&
        services.GetService<IRuntimeHistoryStore>() is { } reader && writer.CanRecordHistory(reader) &&
        services.GetService<IBookmarkResumeDispatcher>()?.CanRecordHistory(writer, reader) == true;

    internal bool SourceOwner(RuntimeHistorySourceSupport owner) => owner.TimerOwner
        ? services.GetService<IRuntimeHistoryTimerSupport>()?.CanCapture == true
        : owner.SourceIntentKind is not { } kind ?
        (ReferenceEquals(owner, RuntimeHistoryRegistration.BoundaryRetrySources)
            ? DefaultBoundaryRetryOwnsSelection()
            : !ReferenceEquals(owner, RuntimeHistoryRegistration.AlterationSources) ||
         services.GetService<IWorkflowAlterationCheckpointWriter>() is RuntimeWorkflowAlterationCheckpointWriter alteration &&
         services.GetService<RuntimeCheckpointCommitter>() is { } committer && alteration.OwnsHistoryCommitter(committer)) :
        services.GetService<IRuntimeHistorySourceExtension>() is { } source &&
        services.GetService<RuntimeCheckpointCommitter>()?.CanSupplyHistorySource(kind, source) == true &&
        services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
        writer.CanRecordHistory(reader) && services.GetService<IRuntimePostCommitIntentDispatcher>()?.CanRecordHistory(kind, writer, reader) == true;

    private bool DefaultBoundaryRetryOwnsSelection()
    {
        foreach (var handler in services.GetServices<IWorkflowSchedulerWorkHandler>())
        {
            if (handler is IFallbackWorkflowSchedulerWorkHandler) continue;
            if (handler is WorkflowRetryActivityBoundarySchedulerWorkHandler) return true;
            // These exact built-ins have closed, different command kinds. An earlier unknown
            // replacement may own retry work, so its registration cannot qualify the unused default.
            var type = handler.GetType();
            if (type != typeof(WorkflowStartSchedulerWorkHandler) && type != typeof(WorkflowScheduleActivitySchedulerWorkHandler) &&
                type != typeof(WorkflowStartActivitySchedulerWorkHandler) && type != typeof(WorkflowCompleteActivitySchedulerWorkHandler) &&
                type != typeof(WorkflowCreateBookmarkSchedulerWorkHandler) && type != typeof(WorkflowCheckpointSchedulerWorkHandler) &&
                type != typeof(WorkflowCancelSchedulerWorkHandler) && type != typeof(WorkflowCancelActivityScopeSchedulerWorkHandler)) return false;
        }
        return false;
    }

    internal static IEnumerable<string> Kinds(RuntimeHistorySourceSupport owner) => owner.Kinds.Select(RuntimeHistorySourceFacts.EventName)
        .Concat(owner.TimerOwner ? DurableTimerHistoryFacts.Kinds : [])
        .Concat(owner.SourceIntentKind is not null && owner.Kinds.Contains(RuntimeHistorySourceFactKind.StimulusPublicationCommitted)
            ? RuntimeHistorySourceFacts.PublicationObservationKinds : [])
        .Concat(owner.SourceIntentKind == WorkflowDispatchLifecycle.StartChildIntentKind && owner.Kinds.Contains(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested)
            ? new[] { "ChildStartAttemptObserved", "ChildStartDispositionObserved" } : []);
}
