using Elsa.Workflows.Runtime.Services.Bookmarks;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointStoreState
{
    internal InMemoryDurableTimerStore.SharedTimerState TimerSources { get; } = new();
}
