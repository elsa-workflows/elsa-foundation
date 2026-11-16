using Elsa.Workflows.Runtime.Core.Contracts;
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
}
