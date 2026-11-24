using Elsa3.Mapping.Services;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// A storage-driver correction: the member converts to Elsa 4's default storage, and the migrator must decide whether that
/// meets what the Elsa 3 driver was chosen for. <paramref name="Metadata"/> names the member and the Elsa 3 driver type, so
/// a report never has to parse the message.
/// </summary>
internal sealed record Elsa3StorageDriverCorrection(string Code, string Message, string Guidance, IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Converts Elsa 3 storage drivers. Every one becomes Elsa 4's default storage (<c>elsa.json</c>, stored with the
/// workflow instance), on variables, inputs and outputs alike, reusable workflow or not. Elsa 3's Workflow Instance
/// driver and its obsolete Workflow driver kept the value with the instance too, so they convert silently. The Memory
/// driver never persisted the value, and a custom driver kept it outside the instance: both convert with a behaviour
/// warning, because what to do instead is the migrator's choice (the storage-driver migration guide walks through it).
/// </summary>
internal static class Elsa3StorageDriverCorrections
{
    /// <summary>The migration guide a storage-driver correction points to.</summary>
    public const string Guide = "docs/migration/elsa3-storage-drivers.md";

    /// <summary>The metadata key holding the Elsa 3 driver type (trimmed, assembly-qualified as exported).</summary>
    public const string StorageDriverTypeKey = "StorageDriverType";

    /// <summary>The metadata key naming the component, so migration tooling can list storage drivers without parsing messages.</summary>
    public const string ComponentNameKey = "ComponentName";

    /// <summary>The metadata key holding the component's kind; for a storage driver <see cref="StorageDriverComponentKind"/>.</summary>
    public const string ComponentKindKey = "ComponentKind";

    /// <summary>The component kind of an Elsa 3 storage driver, Memory or custom.</summary>
    public const string StorageDriverComponentKind = "storage-driver";

    /// <summary>What an Elsa 3 storage driver did with the value.</summary>
    internal enum DriverKind
    {
        /// <summary>No driver, Workflow Instance or the obsolete Workflow driver: the value lived with the workflow instance.</summary>
        WorkflowInstance,

        /// <summary>The Memory driver: the value was never persisted.</summary>
        Memory,

        /// <summary>Any other driver: the value lived wherever that driver put it.</summary>
        Custom
    }

    /// <summary>Classifies an Elsa 3 storage driver type name, with or without its assembly.</summary>
    internal static DriverKind Classify(string? storageDriverTypeName)
    {
        if (string.IsNullOrWhiteSpace(storageDriverTypeName))
            return DriverKind.WorkflowInstance;
        return storageDriverTypeName.Split(',')[0].Trim() switch
        {
            "Elsa.Workflows.WorkflowInstanceStorageDriver" or "WorkflowInstanceStorageDriver" or
                "Elsa.Workflows.Services.WorkflowStorageDriver" or "WorkflowStorageDriver" => DriverKind.WorkflowInstance,
            "Elsa.Workflows.MemoryStorageDriver" or "MemoryStorageDriver" => DriverKind.Memory,
            _ => DriverKind.Custom
        };
    }

    /// <summary>
    /// The correction for a <paramref name="memberKind"/> (<c>Variable</c>, <c>Input</c> or <c>Output</c>) named
    /// <paramref name="memberName"/> on <paramref name="storageDriverTypeName"/>, or <c>null</c> when the driver kept the
    /// value with the workflow instance.
    /// </summary>
    internal static Elsa3StorageDriverCorrection? Describe(string memberKind, string memberName, string? storageDriverTypeName)
    {
        var kind = Classify(storageDriverTypeName);
        if (kind == DriverKind.WorkflowInstance)
            return null;

        var driver = storageDriverTypeName!.Trim();
        var noun = memberKind.ToLowerInvariant();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{memberKind}Name"] = memberName,
            [StorageDriverTypeKey] = driver,
            [ComponentKindKey] = StorageDriverComponentKind,
            [ComponentNameKey] = driver
        };

        if (kind == DriverKind.Memory)
            return new(
                Elsa3ImportDiagnosticCodes.MemoryStoragePersisted,
                $"{memberKind} '{memberName}' used the Elsa 3 Memory storage driver ('{driver}'), which never persisted its value. " +
                $"The {noun} converts to Elsa 4's default storage (elsa.json), so its value is now persisted with the workflow instance.",
                $"Decide why the Memory driver was chosen. If the value must never be persisted (a secret, a token, a large or short-lived value), " +
                $"change the workflow so the {noun} does not hold it; otherwise the default storage is fine. {Guide} walks through the choices.",
                metadata);

        return new(
            Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced,
            $"{memberKind} '{memberName}' used the custom Elsa 3 storage driver '{driver}', which kept its value outside the workflow instance. " +
            $"The converter does not carry storage drivers over: the {noun} converts to Elsa 4's default storage (elsa.json), so its value is now stored with the workflow instance.",
            $"Decide whether the value may live in the workflow instance. If it must stay in the driver's storage (such as a blob store), " +
            $"plan an Elsa 4 solution for that need before the workflow runs in production. {Guide} walks through the choices.",
            metadata);
    }
}
