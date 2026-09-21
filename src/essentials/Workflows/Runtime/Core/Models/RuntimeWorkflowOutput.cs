using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One policy-filtered durable workflow output safe to carry across execution boundaries.</summary>
public sealed record RuntimeWorkflowOutput
{
    public RuntimeWorkflowOutput(
        string name,
        JsonElement? value,
        bool isRedacted,
        string? redactionReason,
        RuntimeValueTypeDescriptor type,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        if (isRedacted && value is not null)
            throw new ArgumentException("A redacted workflow output cannot carry a value.", nameof(value));

        Name = name;
        Value = value?.Clone();
        IsRedacted = isRedacted;
        RedactionReason = redactionReason;
        Type = type;
        CapturedAt = capturedAt;
    }

    public string Name { get; }
    public JsonElement? Value { get; }
    public bool IsRedacted { get; }
    public string? RedactionReason { get; }
    public RuntimeValueTypeDescriptor Type { get; }
    public DateTimeOffset CapturedAt { get; }
}
