using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeDiagnosticsSettings
{
    public const string HostDefaultScope = "host-default";

    public string Scope { get; init; } = HostDefaultScope;

    public RuntimeDiagnosticsEvidenceLevel DefaultLevel { get; init; } = RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot;

    public IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> SubjectOverrides { get; init; } = DefaultSubjectOverrides;

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }

    private static IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> DefaultSubjectOverrides { get; } =
        new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal)
        {
            [RuntimeDiagnosticsSubjects.DurableValues] = RuntimeDiagnosticsEvidenceLevel.Metadata,
            [RuntimeDiagnosticsSubjects.Incidents] = RuntimeDiagnosticsEvidenceLevel.Metadata,
            [RuntimeDiagnosticsSubjects.Diagnostics] = RuntimeDiagnosticsEvidenceLevel.Metadata
        };

    public static RuntimeDiagnosticsSettings Default { get; } = new();
}

public sealed class EffectiveRuntimeDiagnosticsSettings
{
    public RuntimeDiagnosticsEvidenceLevel DefaultLevel { get; init; } = RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot;

    public IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> SubjectOverrides { get; init; } =
        new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> LimitationReasons { get; init; } = [];
}

public sealed class RuntimeDiagnosticsHostPolicy
{
    public RuntimeDiagnosticsEvidenceLevel MaximumLevel { get; init; } = RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot;

    public IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> SubjectMaximums { get; init; } = DefaultSubjectMaximums;

    public IReadOnlyCollection<string> LimitationReasons { get; init; } = ["Full payload capture is disabled by host policy."];

    public DiagnosticSnapshotLimits SnapshotLimits { get; init; } = DiagnosticSnapshotLimits.Default;

    private static IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> DefaultSubjectMaximums { get; } =
        new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal)
        {
            [RuntimeDiagnosticsSubjects.DurableValues] = RuntimeDiagnosticsEvidenceLevel.Metadata,
            [RuntimeDiagnosticsSubjects.Incidents] = RuntimeDiagnosticsEvidenceLevel.Metadata,
            [RuntimeDiagnosticsSubjects.Diagnostics] = RuntimeDiagnosticsEvidenceLevel.Metadata
        };

    public static RuntimeDiagnosticsHostPolicy Default { get; } = new();
}

public sealed class RuntimeDiagnosticsPermissions
{
    public bool CanManage { get; init; } = true;

    public bool CanEnableFullPayloads { get; init; }

    public static RuntimeDiagnosticsPermissions Default { get; } = new();
}

public sealed class RuntimeDiagnosticsSettingsView
{
    public RuntimeDiagnosticsSettings Requested { get; init; } = RuntimeDiagnosticsSettings.Default;

    public EffectiveRuntimeDiagnosticsSettings Effective { get; init; } = new();

    public RuntimeDiagnosticsHostPolicy HostPolicy { get; init; } = RuntimeDiagnosticsHostPolicy.Default;

    public RuntimeDiagnosticsPermissions Permissions { get; init; } = RuntimeDiagnosticsPermissions.Default;
}

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeDiagnosticsEvidenceLevel>))]
public enum RuntimeDiagnosticsEvidenceLevel
{
    Off = 0,
    Metadata = 1,
    DiagnosticSnapshot = 2,
    Payload = 3
}

public static class RuntimeDiagnosticsSubjects
{
    public const string WorkflowInputs = "workflowInputs";
    public const string WorkflowOutputs = "workflowOutputs";
    public const string ActivityInputs = "activityInputs";
    public const string ActivityOutputs = "activityOutputs";
    public const string Incidents = "incidents";
    public const string Diagnostics = "diagnostics";
    public const string ContainerVariables = "containerVariables";
    public const string DurableValues = "durableValues";

    public static readonly IReadOnlyCollection<string> All =
    [
        WorkflowInputs,
        WorkflowOutputs,
        ActivityInputs,
        ActivityOutputs,
        Incidents,
        Diagnostics,
        ContainerVariables,
        DurableValues
    ];

    public static string FromCaptureSubject(RuntimePayloadCaptureSubject subject) =>
        subject switch
        {
            RuntimePayloadCaptureSubject.WorkflowInput => WorkflowInputs,
            RuntimePayloadCaptureSubject.WorkflowOutput => WorkflowOutputs,
            RuntimePayloadCaptureSubject.ActivityInput => ActivityInputs,
            RuntimePayloadCaptureSubject.ActivityOutput => ActivityOutputs,
            RuntimePayloadCaptureSubject.Incident => Incidents,
            RuntimePayloadCaptureSubject.Diagnostic => Diagnostics,
            RuntimePayloadCaptureSubject.ContainerVariable => ContainerVariables,
            RuntimePayloadCaptureSubject.DurableValue => DurableValues,
            _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "Unknown runtime payload capture subject.")
        };
}

public static class RuntimeDiagnosticsSettingsResolver
{
    public static RuntimeDiagnosticsSettingsView Resolve(
        RuntimeDiagnosticsSettings? requested = null,
        RuntimeDiagnosticsHostPolicy? hostPolicy = null,
        RuntimeDiagnosticsPermissions? permissions = null)
    {
        requested ??= RuntimeDiagnosticsSettings.Default;
        hostPolicy ??= RuntimeDiagnosticsHostPolicy.Default;
        permissions ??= RuntimeDiagnosticsPermissions.Default;

        var limitationReasons = new List<string>();
        var effectiveDefault = Cap(requested.DefaultLevel, hostPolicy.MaximumLevel, permissions, limitationReasons, "Default diagnostics level");
        var effectiveOverrides = new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal);

        foreach (var (subject, requestedLevel) in NormalizeSubjectOverrides(requested.SubjectOverrides))
        {
            var subjectMaximum = hostPolicy.SubjectMaximums.TryGetValue(subject, out var configuredMaximum)
                ? Min(configuredMaximum, hostPolicy.MaximumLevel)
                : hostPolicy.MaximumLevel;
            effectiveOverrides[subject] = Cap(requestedLevel, subjectMaximum, permissions, limitationReasons, RuntimeDiagnosticsSubjectDisplayName(subject));
        }

        foreach (var (subject, configuredMaximum) in NormalizeSubjectOverrides(hostPolicy.SubjectMaximums))
        {
            if (effectiveOverrides.ContainsKey(subject))
                continue;

            var subjectMaximum = Min(configuredMaximum, hostPolicy.MaximumLevel);
            var effectiveSubjectLevel = Cap(requested.DefaultLevel, subjectMaximum, permissions, limitationReasons, RuntimeDiagnosticsSubjectDisplayName(subject));
            if (effectiveSubjectLevel != effectiveDefault)
                effectiveOverrides[subject] = effectiveSubjectLevel;
        }

        return new RuntimeDiagnosticsSettingsView
        {
            Requested = Clone(requested),
            Effective = new EffectiveRuntimeDiagnosticsSettings
            {
                DefaultLevel = effectiveDefault,
                SubjectOverrides = effectiveOverrides,
                LimitationReasons = limitationReasons
            },
            HostPolicy = hostPolicy,
            Permissions = permissions
        };
    }

    public static RuntimeDiagnosticsEvidenceLevel GetEffectiveLevel(RuntimeDiagnosticsSettingsView view, RuntimePayloadCaptureSubject subject)
    {
        var subjectId = RuntimeDiagnosticsSubjects.FromCaptureSubject(subject);
        return view.Effective.SubjectOverrides.TryGetValue(subjectId, out var level)
            ? level
            : view.Effective.DefaultLevel;
    }

    public static RuntimePayloadCaptureMode ToCaptureMode(RuntimeDiagnosticsEvidenceLevel level) =>
        level switch
        {
            RuntimeDiagnosticsEvidenceLevel.Off => RuntimePayloadCaptureMode.None,
            RuntimeDiagnosticsEvidenceLevel.Metadata => RuntimePayloadCaptureMode.MetadataOnly,
            RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot => RuntimePayloadCaptureMode.DiagnosticSnapshot,
            RuntimeDiagnosticsEvidenceLevel.Payload => RuntimePayloadCaptureMode.Payload,
            _ => RuntimePayloadCaptureMode.None
        };

    public static RuntimeDiagnosticsSettings Normalize(RuntimeDiagnosticsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new RuntimeDiagnosticsSettings
        {
            Scope = string.IsNullOrWhiteSpace(settings.Scope) ? RuntimeDiagnosticsSettings.HostDefaultScope : settings.Scope,
            DefaultLevel = settings.DefaultLevel,
            SubjectOverrides = NormalizeSubjectOverrides(settings.SubjectOverrides),
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy
        };
    }

    private static RuntimeDiagnosticsSettings Clone(RuntimeDiagnosticsSettings settings) =>
        new()
        {
            Scope = settings.Scope,
            DefaultLevel = settings.DefaultLevel,
            SubjectOverrides = NormalizeSubjectOverrides(settings.SubjectOverrides),
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy
        };

    private static IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel> NormalizeSubjectOverrides(IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel>? subjectOverrides)
    {
        if (subjectOverrides is null || subjectOverrides.Count == 0)
            return new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal);

        var normalized = new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal);
        foreach (var (subject, level) in subjectOverrides)
        {
            if (!RuntimeDiagnosticsSubjects.All.Contains(subject, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown runtime diagnostics subject '{subject}'.", nameof(subjectOverrides));

            if (!Enum.IsDefined(level))
                throw new ArgumentException($"Unknown runtime diagnostics evidence level '{level}'.", nameof(subjectOverrides));

            normalized[subject] = level;
        }

        return normalized;
    }

    private static RuntimeDiagnosticsEvidenceLevel Cap(
        RuntimeDiagnosticsEvidenceLevel requested,
        RuntimeDiagnosticsEvidenceLevel maximum,
        RuntimeDiagnosticsPermissions permissions,
        List<string> limitationReasons,
        string settingName)
    {
        var permissionMaximum = permissions.CanEnableFullPayloads ? maximum : Min(maximum, RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot);
        var effective = Min(requested, permissionMaximum);

        if (effective < requested)
            limitationReasons.Add($"{settingName} was capped at {effective}.");

        return effective;
    }

    private static RuntimeDiagnosticsEvidenceLevel Min(RuntimeDiagnosticsEvidenceLevel first, RuntimeDiagnosticsEvidenceLevel second) =>
        first <= second ? first : second;

    private static string RuntimeDiagnosticsSubjectDisplayName(string subject) =>
        subject switch
        {
            RuntimeDiagnosticsSubjects.WorkflowInputs => "Workflow inputs",
            RuntimeDiagnosticsSubjects.WorkflowOutputs => "Workflow outputs",
            RuntimeDiagnosticsSubjects.ActivityInputs => "Activity inputs",
            RuntimeDiagnosticsSubjects.ActivityOutputs => "Activity outputs",
            RuntimeDiagnosticsSubjects.Incidents => "Incidents",
            RuntimeDiagnosticsSubjects.Diagnostics => "Diagnostics",
            RuntimeDiagnosticsSubjects.ContainerVariables => "Container variables",
            RuntimeDiagnosticsSubjects.DurableValues => "Durable values",
            _ => subject
        };
}
