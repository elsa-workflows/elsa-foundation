using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Declared durable runtime value state. Raw activity outputs become durable only by capture into this model.
/// </summary>
public sealed record DurableValueState(
    string DurableValueId,
    string WorkflowExecutionId,
    string ValueId,
    RuntimeValueTypeDescriptor Type,
    DurableValueLifecycle Lifecycle,
    DurableValueStorage? Storage,
    JsonElement? InlineValue,
    DurableValueExternalReference? ExternalReference,
    string? SourceActivityExecutionId,
    DateTimeOffset CapturedAt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RuntimeValueTypeDescriptor(
    string Kind,
    string? Id,
    JsonElement? Schema);

public sealed record DurableValueExternalReference(
    string StorageProfile,
    string Locator,
    IReadOnlyDictionary<string, string> Metadata);

public enum DurableValueLifecycle
{
    None,
    Instance,
    Result,
    Audit,
    Custom
}

public enum DurableValueStorage
{
    Inline,
    External,
    Custom
}
