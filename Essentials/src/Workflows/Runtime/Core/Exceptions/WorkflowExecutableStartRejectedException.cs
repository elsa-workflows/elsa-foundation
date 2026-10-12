namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// A safe, machine-classifiable rejection of a future executable start before actor lookup or state creation.
/// </summary>
public sealed class WorkflowExecutableStartRejectedException : Exception
{
    public WorkflowExecutableStartRejectedException(
        WorkflowExecutableStartRejectionCategory category,
        string reasonCode,
        string message)
        : base(message)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown executable start rejection category.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Category = category;
        ReasonCode = reasonCode;
    }

    public WorkflowExecutableStartRejectionCategory Category { get; }
    public string ReasonCode { get; }
}

public enum WorkflowExecutableStartRejectionCategory
{
    Authority = 0,
    Policy = 1
}
