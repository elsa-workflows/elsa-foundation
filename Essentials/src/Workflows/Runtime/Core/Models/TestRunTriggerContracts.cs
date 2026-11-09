using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Server-resolved context for one eligible trigger binding in a compiled executable.</summary>
public sealed record TestRunTriggerContext(
    WorkflowExecutable Executable,
    string ProviderId,
    ExecutableNode Node,
    WorkflowTriggerBinding Binding);

/// <summary>A driver mode the designer can offer for one selected trigger binding.</summary>
public sealed class TestRunTriggerMode
{
    public TestRunTriggerMode(
        string modeId,
        string label,
        string description,
        bool isDefault,
        int configurationVersion = 1,
        JsonElement? configurationSchema = null,
        JsonElement? configuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(description);
        if (configurationVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationVersion));
        if (configurationSchema is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A configuration schema must be a defined JSON value.", nameof(configurationSchema));
        if (configuration is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A read-only configuration must be a defined JSON value.", nameof(configuration));

        ModeId = modeId;
        Label = label;
        Description = description;
        IsDefault = isDefault;
        ConfigurationVersion = configurationVersion;
        ConfigurationSchema = configurationSchema?.Clone();
        Configuration = configuration?.Clone();
    }

    public string ModeId { get; }
    public string Label { get; }
    public string Description { get; }
    public bool IsDefault { get; }
    public int ConfigurationVersion { get; }

    /// <summary>The JSON Schema for caller-supplied mode data, when the mode accepts data.</summary>
    public JsonElement? ConfigurationSchema { get; }

    /// <summary>Safe read-only module context such as an event name, route, or authored schedule.</summary>
    public JsonElement? Configuration { get; }
}

/// <summary>A safe validation result that never echoes rejected user data.</summary>
public sealed class TestRunTriggerValidation
{
    private TestRunTriggerValidation(IReadOnlyCollection<TestRunTriggerFinding> findings) =>
        Findings = findings;

    public bool IsValid => Findings.Count == 0;
    public IReadOnlyCollection<TestRunTriggerFinding> Findings { get; }

    public static TestRunTriggerValidation Success() => new(Array.Empty<TestRunTriggerFinding>());

    public static TestRunTriggerValidation Failure(IEnumerable<TestRunTriggerFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var snapshot = findings.ToArray();
        if (snapshot.Length == 0)
            throw new ArgumentException("A failed validation must contain at least one finding.", nameof(findings));
        return new TestRunTriggerValidation(Array.AsReadOnly(snapshot));
    }
}

public sealed record TestRunTriggerFinding(string Code, string Message, string? Field = null);

/// <summary>Generic action descriptor returned by an external protocol driver.</summary>
public sealed class TestRunDriverAction
{
    public TestRunDriverAction(
        string kind,
        string relativeUrl,
        string method,
        JsonElement? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var path = relativeUrl.Split('?', 2)[0];
        var decodedPath = Uri.UnescapeDataString(path);
        if (!relativeUrl.StartsWith("/", StringComparison.Ordinal) ||
            relativeUrl.StartsWith("//", StringComparison.Ordinal) ||
            relativeUrl.Contains('\\') ||
            relativeUrl.Contains('\r') ||
            relativeUrl.Contains('\n') ||
            relativeUrl.Contains('#') ||
            decodedPath.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("A driver action URL must be an API-root-relative path without an authority or traversal segment.", nameof(relativeUrl));
        if (details is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Action details must be a defined JSON value.", nameof(details));

        Kind = kind;
        RelativeUrl = relativeUrl;
        Method = method;
        Details = details?.Clone();
    }

    public string Kind { get; }
    public string RelativeUrl { get; }
    public string Method { get; }
    public JsonElement? Details { get; }
}

public enum TestRunPreparationKind
{
    Immediate = 0,
    ExternalAction = 1
}

/// <summary>Result of driver preparation, either an immediate logical event or a later external action.</summary>
public sealed class TestRunTriggerPreparation
{
    private TestRunTriggerPreparation(
        TestRunPreparationKind kind,
        JsonElement? stimulusInput,
        TestRunDriverAction? action,
        JsonElement? details,
        JsonElement? driverState)
    {
        if (kind == TestRunPreparationKind.Immediate && action is not null)
            throw new ArgumentException("An immediate preparation cannot carry an external action.", nameof(action));
        if (kind == TestRunPreparationKind.ExternalAction && action is null)
            throw new ArgumentNullException(nameof(action));
        if (kind == TestRunPreparationKind.ExternalAction && stimulusInput is not null)
            throw new ArgumentException("An external action cannot carry a start stimulus.", nameof(stimulusInput));
        if (details is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Preparation details must be a defined JSON value.", nameof(details));
        if (driverState is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("Driver state must be a defined JSON value.", nameof(driverState));

        Kind = kind;
        StimulusInput = stimulusInput?.Clone();
        Action = action;
        Details = details?.Clone();
        DriverState = driverState?.Clone();
    }

    public TestRunPreparationKind Kind { get; }

    /// <summary>May be null for an immediate trigger whose stimulus payload is null.</summary>
    public JsonElement? StimulusInput { get; }

    public TestRunDriverAction? Action { get; }

    /// <summary>Safe details that may be returned to the designer.</summary>
    public JsonElement? Details { get; }

    /// <summary>Opaque driver-owned state retained only in the bounded target projection.</summary>
    public JsonElement? DriverState { get; }

    public static TestRunTriggerPreparation Immediate(
        JsonElement? stimulusInput = null,
        JsonElement? details = null) =>
        new(TestRunPreparationKind.Immediate, stimulusInput, null, details, null);

    public static TestRunTriggerPreparation External(
        TestRunDriverAction action,
        JsonElement? details = null,
        JsonElement? driverState = null) =>
        new(TestRunPreparationKind.ExternalAction, null, action, details, driverState);
}

public sealed record TestRunTriggerPrepareContext(
    TestRunTriggerContext Trigger,
    WorkflowTestScope Scope,
    string TestRunId,
    string ModeId,
    JsonElement? Data,
    string TargetId);

public sealed record TestRunTriggerStopContext(
    TestRunTriggerContext Trigger,
    WorkflowTestScope Scope,
    string TestRunId,
    string ModeId,
    JsonElement? DriverState);
