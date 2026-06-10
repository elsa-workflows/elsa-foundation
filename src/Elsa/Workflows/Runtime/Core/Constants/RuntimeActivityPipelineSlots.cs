using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Constants;

public static class RuntimeActivityPipelineSlots
{
    public const string LoadState = nameof(LoadState);
    public const string InputEvaluation = nameof(InputEvaluation);
    public const string Invoke = nameof(Invoke);
    public const string OutputCapture = nameof(OutputCapture);
    public const string Scheduling = nameof(Scheduling);
    public const string Checkpoint = nameof(Checkpoint);
    public const string PostCommit = nameof(PostCommit);

    public static readonly IReadOnlyList<RuntimePipelineSlotDefinition> All =
    [
        new(LoadState, 100),
        new(InputEvaluation, 200),
        new(Invoke, 300),
        new(OutputCapture, 400),
        new(Scheduling, 500),
        new(Checkpoint, 600),
        new(PostCommit, 700)
    ];
}
