using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
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

    internal bool SourceOwner(RuntimeHistorySourceSupport owner) => owner.SourceIntentKind is not { } kind ||
        services.GetService<IRuntimeHistorySourceExtension>() is { } source &&
        services.GetService<RuntimeCheckpointCommitter>()?.CanSupplyHistorySource(kind, source) == true &&
        services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
        writer.CanRecordHistory(reader) && services.GetService<IRuntimePostCommitIntentDispatcher>()?.CanRecordHistory(kind, writer, reader) == true;

    internal static IEnumerable<string> Kinds(RuntimeHistorySourceSupport owner) => owner.Kinds.Select(RuntimeHistorySourceFacts.EventName)
        .Concat(owner.SourceIntentKind is not null && owner.Kinds.Contains(RuntimeHistorySourceFactKind.StimulusPublicationCommitted)
            ? RuntimeHistorySourceFacts.PublicationObservationKinds : []);
}
