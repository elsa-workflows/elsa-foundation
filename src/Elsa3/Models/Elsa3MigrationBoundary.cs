using System.Collections.ObjectModel;

namespace Elsa3.Models;

public sealed class Elsa3WorkflowDefinitionImportInput
{
    public Elsa3WorkflowDefinitionImportInput(
        Elsa3MigrationInputKind inputKind,
        Elsa3WorkflowDefinition definition,
        string? sourceName = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!Elsa3MigrationCompatibility.IsAuthoredDefinitionInput(inputKind))
            throw new ArgumentException("Only Elsa 3 authored workflow definition inputs are accepted for definition import.", nameof(inputKind));

        if (sourceName is not null && string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Migration source name cannot be blank.", nameof(sourceName));

        InputKind = inputKind;
        Definition = definition;
        SourceName = sourceName;
        Metadata = Elsa3MigrationMetadata.Snapshot(metadata);
    }

    public Elsa3MigrationInputKind InputKind { get; }
    public Elsa3WorkflowDefinition Definition { get; }
    public string? SourceName { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class Elsa3MigrationDiagnostic
{
    public Elsa3MigrationDiagnostic(
        Elsa3MigrationDiagnosticSeverity severity,
        string code,
        string message,
        string? path = null,
        string? guidance = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (path is not null && string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Diagnostic path cannot be blank.", nameof(path));

        if (guidance is not null && string.IsNullOrWhiteSpace(guidance))
            throw new ArgumentException("Diagnostic guidance cannot be blank.", nameof(guidance));

        Severity = severity;
        Code = code;
        Message = message;
        Path = path;
        Guidance = guidance;
        Metadata = Elsa3MigrationMetadata.Snapshot(metadata);
    }

    public Elsa3MigrationDiagnosticSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public string? Path { get; }
    public string? Guidance { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public bool IsError => Severity == Elsa3MigrationDiagnosticSeverity.Error;
}

public sealed class Elsa3MigrationResult<T>
{
    private Elsa3MigrationResult(T? value, IReadOnlyCollection<Elsa3MigrationDiagnostic> diagnostics)
    {
        Value = value;
        Diagnostics = diagnostics;
        Succeeded = diagnostics.All(diagnostic => !diagnostic.IsError);
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public IReadOnlyCollection<Elsa3MigrationDiagnostic> Diagnostics { get; }

    public static Elsa3MigrationResult<T> Success(T value, IEnumerable<Elsa3MigrationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        var snapshot = SnapshotDiagnostics(diagnostics);
        if (snapshot.Any(diagnostic => diagnostic.IsError))
            throw new ArgumentException("Successful Elsa 3 migration results cannot carry error diagnostics.", nameof(diagnostics));

        return new Elsa3MigrationResult<T>(value, snapshot);
    }

    public static Elsa3MigrationResult<T> Failure(IEnumerable<Elsa3MigrationDiagnostic> diagnostics)
    {
        var snapshot = SnapshotDiagnostics(diagnostics);
        if (snapshot.Count == 0)
            throw new ArgumentException("Failed Elsa 3 migration results require at least one diagnostic.", nameof(diagnostics));

        if (snapshot.All(diagnostic => !diagnostic.IsError))
            throw new ArgumentException("Failed Elsa 3 migration results require at least one error diagnostic.", nameof(diagnostics));

        return new Elsa3MigrationResult<T>(default, snapshot);
    }

    public static Elsa3MigrationResult<T> Failure(params Elsa3MigrationDiagnostic[] diagnostics) => Failure((IEnumerable<Elsa3MigrationDiagnostic>)diagnostics);

    private static IReadOnlyCollection<Elsa3MigrationDiagnostic> SnapshotDiagnostics(IEnumerable<Elsa3MigrationDiagnostic>? diagnostics)
    {
        var snapshot = (diagnostics ?? []).ToArray();
        if (snapshot.Any(diagnostic => diagnostic is null))
            throw new ArgumentException("Migration diagnostics cannot contain null entries.", nameof(diagnostics));

        return snapshot;
    }
}

public static class Elsa3MigrationCompatibility
{
    public static bool IsAuthoredDefinitionInput(Elsa3MigrationInputKind inputKind) =>
        inputKind is Elsa3MigrationInputKind.WorkflowDefinitionExportJson
            or Elsa3MigrationInputKind.WorkflowDefinitionOriginalSource
            or Elsa3MigrationInputKind.WorkflowDefinitionStringDataRoot;

    public static bool IsLiveInstanceStateInput(Elsa3MigrationInputKind inputKind) =>
        inputKind == Elsa3MigrationInputKind.WorkflowInstanceState;

    public static Elsa3MigrationResult<T> RejectUnsupportedInputKind<T>(Elsa3MigrationInputKind inputKind, string? sourceName = null)
    {
        if (sourceName is not null && string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Migration source name cannot be blank.", nameof(sourceName));

        var metadata = new Dictionary<string, string>
        {
            ["InputKind"] = inputKind.ToString()
        };

        if (sourceName is not null)
            metadata["SourceName"] = sourceName;

        return Elsa3MigrationResult<T>.Failure(new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.UnsupportedInputKind,
            $"Elsa 3 migration input kind '{inputKind}' is not supported by this boundary.",
            guidance: "Use authored Elsa 3 workflow definition input for definition import. Elsa 3 workflow instance state requires a separate explicit migration tool.",
            metadata: metadata));
    }

    public static Elsa3MigrationResult<T> RejectLiveInstanceResume<T>(string? sourceName = null)
    {
        if (sourceName is not null && string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("Migration source name cannot be blank.", nameof(sourceName));

        var metadata = sourceName is null
            ? null
            : new Dictionary<string, string> { ["SourceName"] = sourceName };

        return Elsa3MigrationResult<T>.Failure(new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.LiveInstanceResumeUnsupported,
            "Elsa 3 workflow instance state cannot be resumed as Elsa 4 runtime execution state.",
            guidance: "Drain or complete Elsa 3 workflow instances before cutover, or run an explicit external migration tool that creates native Elsa 4 workflow executions.",
            metadata: metadata));
    }
}

public static class Elsa3MigrationDiagnosticCodes
{
    public const string UnsupportedInputKind = "ELS3-MIGRATION-UNSUPPORTED-INPUT";
    public const string LiveInstanceResumeUnsupported = "ELS3-MIGRATION-LIVE-INSTANCE-UNSUPPORTED";
    public const string DefinitionMappingFailed = "ELS3-MIGRATION-DEFINITION-MAPPING-FAILED";
}

public enum Elsa3MigrationInputKind
{
    WorkflowDefinitionExportJson,
    WorkflowDefinitionOriginalSource,
    WorkflowDefinitionStringDataRoot,
    WorkflowInstanceState
}

public enum Elsa3MigrationDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

internal static class Elsa3MigrationMetadata
{
    public static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? metadata) =>
        new ReadOnlyDictionary<string, string>((metadata ?? new Dictionary<string, string>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
