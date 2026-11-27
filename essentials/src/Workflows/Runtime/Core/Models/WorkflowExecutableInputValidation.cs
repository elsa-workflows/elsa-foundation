using System.Collections.ObjectModel;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A detached normalized input bag or safe validation findings.</summary>
public sealed class WorkflowExecutableInputValidation
{
    private WorkflowExecutableInputValidation(
        IReadOnlyDictionary<string, JsonElement> normalizedInputs,
        IReadOnlyCollection<WorkflowExecutableInputValidationFinding> findings)
    {
        NormalizedInputs = normalizedInputs;
        Findings = findings;
    }

    public bool IsValid => Findings.Count == 0;
    public IReadOnlyDictionary<string, JsonElement> NormalizedInputs { get; }
    public IReadOnlyCollection<WorkflowExecutableInputValidationFinding> Findings { get; }

    public static WorkflowExecutableInputValidation Success(IEnumerable<KeyValuePair<string, JsonElement>> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var snapshot = inputs
            .OrderBy(input => input.Key, StringComparer.Ordinal)
            .ToDictionary(input => input.Key, input => input.Value.Clone(), StringComparer.Ordinal);

        return new WorkflowExecutableInputValidation(
            new ReadOnlyDictionary<string, JsonElement>(snapshot),
            []);
    }

    public static WorkflowExecutableInputValidation Failure(IEnumerable<WorkflowExecutableInputValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var snapshot = findings.ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("A failed input validation must contain at least one finding.", nameof(findings));

        return new WorkflowExecutableInputValidation(
            new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>(StringComparer.Ordinal)),
            Array.AsReadOnly(snapshot));
    }
}

/// <summary>A machine-classifiable finding that never carries a rejected input value.</summary>
public sealed class WorkflowExecutableInputValidationFinding
{
    public WorkflowExecutableInputValidationFinding(string reasonCode, string message, string? inputName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ReasonCode = reasonCode;
        Message = message;
        InputName = inputName;
    }

    public string ReasonCode { get; }
    public string Message { get; }
    public string? InputName { get; }
}

/// <summary>Stable reason codes emitted by executable input validation.</summary>
public static class WorkflowExecutableInputValidationReasonCodes
{
    public const string UnsupportedContractVersion = "runtime.input.unsupported-contract-version";
    public const string BlankName = "runtime.input.blank-name";
    public const string UnknownName = "runtime.input.unknown-name";
    public const string DuplicateName = "runtime.input.duplicate-name";
    public const string MissingRequired = "runtime.input.missing-required";
    public const string UnknownTypeAlias = "runtime.input.unknown-type-alias";
    public const string IncompatibleValue = "runtime.input.incompatible-value";
}
