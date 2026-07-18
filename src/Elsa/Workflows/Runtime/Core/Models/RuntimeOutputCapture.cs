namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Declaration that promotes an execution-local activity result projection into a declared durable value.
/// </summary>
public sealed class RuntimeOutputCapture
{
    public RuntimeOutputCapture(
        string outputName,
        string valueId,
        RuntimeValueTypeDescriptor type,
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage,
        bool captureOnSuccessfulCompletion,
        IReadOnlyDictionary<string, string>? metadata = null,
        string storageDriverKey = WellKnownRuntimeDurableValueStorageDrivers.Json,
        ValueConversionPlan? conversionPlan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueId);
        ArgumentNullException.ThrowIfNull(type);

        if (lifecycle == DurableValueLifecycle.None)
            throw new ArgumentException("Output capture must target a durable value lifecycle.", nameof(lifecycle));
        if (storage == DurableValueStorage.None)
            throw new ArgumentException("Output capture must declare a durable storage strategy.", nameof(storage));

        ArgumentException.ThrowIfNullOrWhiteSpace(storageDriverKey);

        OutputName = outputName;
        ValueId = valueId;
        Type = type;
        Lifecycle = lifecycle;
        Storage = storage;
        CaptureOnSuccessfulCompletion = captureOnSuccessfulCompletion;
        StorageDriverKey = storageDriverKey;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ConversionPlan = conversionPlan;
    }

    public string OutputName { get; }
    public string ValueId { get; }
    public RuntimeValueTypeDescriptor Type { get; }
    public DurableValueLifecycle Lifecycle { get; }
    public DurableValueStorage Storage { get; }
    public bool CaptureOnSuccessfulCompletion { get; }
    public string StorageDriverKey { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    /// <summary>Null preserves the legacy identity/retyping behavior of artifacts published before conversion plans.</summary>
    public ValueConversionPlan? ConversionPlan { get; }
}
