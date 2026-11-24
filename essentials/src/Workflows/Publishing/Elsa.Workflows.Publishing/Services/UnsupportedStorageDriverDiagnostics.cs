using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Refusals for a storage driver chosen where publication does not implement drivers (#5698). Only a reusable activity's
/// own inputs, outputs and graph variables are stored through their driver. Everywhere else a driver other than the
/// built-in <c>elsa.json</c> is refused, so a value is never silently stored inline against the author's choice.
/// </summary>
internal static class UnsupportedStorageDriverDiagnostics
{
    /// <summary>True for the built-in JSON driver, or for no driver, which means the same.</summary>
    public static bool IsBuiltIn(string? storageDriverType) =>
        string.IsNullOrWhiteSpace(storageDriverType) ||
        StringComparer.Ordinal.Equals(storageDriverType, WellKnownRuntimeDurableValueStorageDrivers.Json);

    public static ArgumentException Variable(string nodeId, string variableKey, string storageDriverType) => new(
        $"VF-ACT-005: Variable '{variableKey}' on activity node '{nodeId}' uses storage driver '{storageDriverType}'. " +
        $"Workflow and container variables support only the built-in '{WellKnownRuntimeDurableValueStorageDrivers.Json}' driver; " +
        "other storage drivers are not implemented for them yet.");

    public static ArgumentException WorkflowInput(string inputName, string storageDriverType) =>
        WorkflowMember("input", inputName, storageDriverType);

    public static ArgumentException WorkflowOutput(string outputName, string storageDriverType) =>
        WorkflowMember("output", outputName, storageDriverType);

    private static ArgumentException WorkflowMember(string kind, string name, string storageDriverType) => new(
        $"VF-ACT-005: Workflow {kind} '{name}' uses storage driver '{storageDriverType}'. " +
        $"A workflow's own inputs and outputs support only the built-in '{WellKnownRuntimeDurableValueStorageDrivers.Json}' driver; " +
        "other storage drivers are not implemented for them yet.");
}
