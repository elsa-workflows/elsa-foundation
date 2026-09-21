namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Output value recorded during one activity invocation before it is published or captured.
/// </summary>
public sealed class RecordedActivityOutput
{
    public RecordedActivityOutput(string outputName, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        OutputName = outputName;
        Value = value;
    }

    public string OutputName { get; }
    public object? Value { get; }
}
