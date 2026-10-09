namespace Elsa.Canary.Fixtures;

/// <summary>One activation of a canary activity: the run, the step and what <c>Primary</c> and <c>Plain</c> held.</summary>
public sealed record CanaryActivation(string WorkflowExecutionId, string Step, string? Primary, string? Plain);

/// <summary>
/// Collects every activation of the canary activities, in the order they ran. They record here and nowhere else, so
/// the positive control that an activity received a value reads nothing a surface could hold.
/// </summary>
public sealed class CanaryRecorder
{
    public const string Execute = "execute";
    public const string Resume = "resume";
    public const string ChildCompleted = "child-completed";

    private readonly List<CanaryActivation> _activations = [];

    public void Add(string workflowExecutionId, string step, string? primary, string? plain)
    {
        lock (_activations)
            _activations.Add(new(workflowExecutionId, step, primary, plain));
    }

    /// <summary>The activations of <paramref name="workflowExecutionId"/>, in the order they ran.</summary>
    public IReadOnlyList<CanaryActivation> For(string workflowExecutionId)
    {
        lock (_activations)
            return _activations.Where(activation => activation.WorkflowExecutionId == workflowExecutionId).ToArray();
    }
}
