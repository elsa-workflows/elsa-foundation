using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Constants;

public static class RuntimeWorkflowPipelineSlots
{
    public const string LoadState = nameof(LoadState);
    public const string Invoke = nameof(Invoke);
    public const string Scheduling = nameof(Scheduling);
    public const string Checkpoint = nameof(Checkpoint);
    public const string PostCommit = nameof(PostCommit);

    public static readonly IReadOnlyList<RuntimePipelineSlotDefinition> All = Array.AsReadOnly<RuntimePipelineSlotDefinition>([
        new(LoadState, 100),
        new(Invoke, 150),
        new(Scheduling, 200),
        new(Checkpoint, 300),
        new(PostCommit, 400)
    ]);
}
