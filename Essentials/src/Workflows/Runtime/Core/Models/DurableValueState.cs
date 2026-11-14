using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Declared durable runtime value state for variables, workflow inputs, and workflow outputs.
/// Activity results use their own atomic completion envelope and are not copied into this model.
/// </summary>
public sealed record DurableValueState
{
    public DurableValueState(
        string durableValueId,
        string workflowExecutionId,
        string valueId,
        RuntimeValueTypeDescriptor type,
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        string? sourceActivityExecutionId,
        DateTimeOffset capturedAt,
        IReadOnlyDictionary<string, string> metadata)
    {
        Validate(lifecycle, storage, inlineValue, externalReference);

        DurableValueId = durableValueId;
        WorkflowExecutionId = workflowExecutionId;
        ValueId = valueId;
        Type = type;
        Lifecycle = lifecycle;
        Storage = storage;
        InlineValue = inlineValue;
        ExternalReference = externalReference;
        SourceActivityExecutionId = sourceActivityExecutionId;
        CapturedAt = capturedAt;
        Metadata = metadata;
    }

    public string DurableValueId { get; }
    public string WorkflowExecutionId { get; }
    public string ValueId { get; }
    public RuntimeValueTypeDescriptor Type { get; }
    public DurableValueLifecycle Lifecycle { get; }
    public DurableValueStorage Storage { get; }
    public JsonElement? InlineValue { get; }
    public DurableValueExternalReference? ExternalReference { get; }
    public string? SourceActivityExecutionId { get; }
    public DateTimeOffset CapturedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static void Validate(
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference)
    {
        if (lifecycle == DurableValueLifecycle.None)
        {
            if (storage != DurableValueStorage.None)
                throw new ArgumentException("A non-durable value must use the None storage strategy.", nameof(storage));

            if (inlineValue.HasValue)
                throw new ArgumentException("A non-durable value cannot carry inline durable state.", nameof(inlineValue));

            if (externalReference is not null)
                throw new ArgumentException("A non-durable value cannot carry an external durable reference.", nameof(externalReference));

            return;
        }

        if (storage == DurableValueStorage.None)
            throw new ArgumentException("A durable value lifecycle requires a storage strategy.", nameof(storage));

        if (storage == DurableValueStorage.Inline)
        {
            if (!inlineValue.HasValue)
                throw new ArgumentException("Inline durable value state requires an inline value.", nameof(inlineValue));

            if (externalReference is not null)
                throw new ArgumentException("Inline durable value state cannot also carry an external reference.", nameof(externalReference));
        }

        if (storage == DurableValueStorage.External)
        {
            if (externalReference is null)
                throw new ArgumentException("External durable value state requires an external reference.", nameof(externalReference));

            if (inlineValue.HasValue)
                throw new ArgumentException("External durable value state cannot also carry an inline value.", nameof(inlineValue));
        }

        if (storage == DurableValueStorage.Custom && inlineValue.HasValue && externalReference is not null)
            throw new ArgumentException("Custom durable value state cannot carry both inline value and external reference.", nameof(externalReference));
    }
}

public sealed record RuntimeValueTypeDescriptor(
    string Kind,
    string? Id,
    JsonElement? Schema);

public sealed record DurableValueExternalReference
{
    private string _storageProfile = null!;
    private string _locator = null!;
    private IReadOnlyDictionary<string, string> _metadata = null!;

    public DurableValueExternalReference(
        string StorageProfile,
        string Locator,
        IReadOnlyDictionary<string, string> Metadata)
    {
        this.StorageProfile = StorageProfile;
        this.Locator = Locator;
        this.Metadata = Metadata;
    }

    public string StorageProfile
    {
        get => _storageProfile;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(StorageProfile));
            _storageProfile = value;
        }
    }

    public string Locator
    {
        get => _locator;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Locator));
            _locator = value;
        }
    }

    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Metadata));
            _metadata = RuntimeModelMetadata.Snapshot(value);
        }
    }

    public void Deconstruct(
        out string StorageProfile,
        out string Locator,
        out IReadOnlyDictionary<string, string> Metadata)
    {
        StorageProfile = this.StorageProfile;
        Locator = this.Locator;
        Metadata = this.Metadata;
    }
}

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
    None,
    Inline,
    External,
    Custom
}
