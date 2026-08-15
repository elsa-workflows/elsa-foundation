using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class RuntimeRequirementPreflightRequestException(string message) : Exception(message);

/// <summary>
/// Checks active retained artifacts against installed Runtime capability registries. Explicit artifact
/// selection is a narrowing filter over that retained set; it never turns this endpoint into an arbitrary
/// artifact-store lookup.
/// </summary>
public sealed class RuntimeRequirementPreflight(
    IWorkflowExecutableSourceReferenceReader sourceReferences,
    IWorkflowExecutableStore workflowExecutables,
    IExecutableActivityTemplateReader activityTemplates,
    IRuntimeRequirementChecker requirementChecker,
    TimeProvider timeProvider)
{
    public const string ActiveRetainedArtifactsScope = "ActiveRetainedArtifacts";

    public async ValueTask<RuntimeRequirementPreflightView> RunAsync(
        string scope,
        IReadOnlyList<string>? artifactIds,
        CancellationToken cancellationToken = default)
    {
        var selection = ValidateSelection(scope, artifactIds);
        var asOf = timeProvider.GetUtcNow();
        var references = await sourceReferences.ListAllAsync(liveOnly: true, now: asOf, cancellationToken: cancellationToken);
        var retained = references
            .GroupBy(x => x.ArtifactId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var selectedIds = selection ?? retained.Keys.Order(StringComparer.Ordinal).ToArray();
        var artifacts = new List<RuntimeArtifactPreflight>(selectedIds.Count);

        foreach (var artifactId in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!retained.ContainsKey(artifactId))
            {
                artifacts.Add(new(artifactId, false, false, []));
                continue;
            }

            var subject = await LoadRequirementsAsync(artifactId, cancellationToken);
            artifacts.Add(new(
                artifactId,
                true,
                subject is not null,
                subject is null ? [] : CheckCapabilities(subject)));
        }

        var requirements = artifacts
            .SelectMany(artifact => artifact.Capabilities
                .Where(capability => capability.Kind == RuntimeCapabilityKind.ActivityConsumer)
                .Select(capability => (artifact.ArtifactId, Capability: capability)))
            .GroupBy(x => (x.Capability.Key, x.Capability.SchemaVersion), StringTupleComparer.Instance)
            .OrderBy(x => x.Key.Key, StringComparer.Ordinal)
            .ThenBy(x => x.Key.SchemaVersion, StringComparer.Ordinal)
            .Select(group => new RuntimeRequirementPreflightItemView(
                group.Key.Key,
                group.Key.SchemaVersion!,
                WorstStatus(group.Select(x => x.Capability.Status)).ToString(),
                group.Select(x => x.ArtifactId).Distinct(StringComparer.Ordinal).Count()))
            .ToArray();
        var diagnostics = BuildDiagnostics(artifacts);
        var isReady = artifacts.All(x => x.IsRetained && x.IsAvailable &&
                                                   x.Capabilities.All(capability => capability.Status == RuntimeCapabilityStatus.Available));
        return new(artifacts.Count, isReady, requirements, diagnostics);
    }

    private static IReadOnlyList<string>? ValidateSelection(string scope, IReadOnlyList<string>? artifactIds)
    {
        if (!StringComparer.Ordinal.Equals(scope, ActiveRetainedArtifactsScope))
            throw new RuntimeRequirementPreflightRequestException($"Scope must be '{ActiveRetainedArtifactsScope}'.");
        if (artifactIds is null)
            return null;
        if (artifactIds.Count == 0 || artifactIds.Any(string.IsNullOrWhiteSpace))
            throw new RuntimeRequirementPreflightRequestException("artifactIds must be null or contain non-empty artifact identifiers.");
        if (artifactIds.Distinct(StringComparer.Ordinal).Count() != artifactIds.Count)
            throw new RuntimeRequirementPreflightRequestException("artifactIds must not contain duplicate identifiers.");
        return artifactIds.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Resolves the artifact's requirement set from the executable store, falling back to the
    /// reusable-activity template store. Returns null when neither knows the artifact.
    /// </summary>
    /// <remarks>
    /// The template fallback is preserved capability, not publishing residue — the shared checker
    /// contract accepts template requirement sets precisely so this wrapper loses nothing by
    /// delegating (2026-08-15 architect review, FR-B-005).
    /// </remarks>
    private async ValueTask<RuntimeRequirementCheckSubject?> LoadRequirementsAsync(
        string artifactId,
        CancellationToken cancellationToken)
    {
        var executable = await workflowExecutables.FindAsync(artifactId, cancellationToken);
        if (executable is not null)
            return RuntimeRequirementCheckSubject.FromExecutable(executable);

        var template = await activityTemplates.FindAsync(artifactId, cancellationToken);
        return template is null ? null : RuntimeRequirementCheckSubject.FromTemplate(template);
    }

    /// <summary>
    /// Projects the shared runtime verdict into this endpoint's view vocabulary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evaluation itself now lives in <see cref="IRuntimeRequirementChecker"/> (FR-B-005): the
    /// logic moved to the Runtime layer unchanged, and this wrapper keeps the retained-set scoping,
    /// the view shapes and the diagnostics that are genuinely publishing concerns.
    /// </para>
    /// <para>
    /// The checker's third axis — per-node CLR activity-type presence — is deliberately NOT
    /// projected here. That axis is the <em>import</em> gate's (FR-B-005a), and this endpoint's
    /// <c>RuntimeCapabilityStatus</c> has no member for it: surfacing it would emit a new
    /// <c>Status</c> string over the wire and change a published contract. Preserving this
    /// endpoint's behaviour exactly is the §2.21.1 obligation on the extraction.
    /// </para>
    /// </remarks>
    private IReadOnlyList<RuntimeCapabilityPreflight> CheckCapabilities(RuntimeRequirementCheckSubject subject)
    {
        var verdict = requirementChecker.Check(subject);
        var result = new List<RuntimeCapabilityPreflight>(verdict.Requirements.Count + verdict.StorageDrivers.Count);

        result.AddRange(verdict.Requirements.Select(entry => new RuntimeCapabilityPreflight(
            RuntimeCapabilityKind.ActivityConsumer,
            entry.ConsumerKey,
            entry.SchemaVersion,
            ToViewStatus(entry.Status),
            entry.SupportedSchemaVersions)));

        result.AddRange(verdict.StorageDrivers.Select(entry => new RuntimeCapabilityPreflight(
            RuntimeCapabilityKind.DurableValueStorageDriver,
            entry.DriverKey,
            null,
            ToViewStatus(entry.Status),
            [])));

        return result;
    }

    private static RuntimeCapabilityStatus ToViewStatus(RuntimeRequirementStatus status) => status switch
    {
        RuntimeRequirementStatus.Available => RuntimeCapabilityStatus.Available,
        RuntimeRequirementStatus.Missing => RuntimeCapabilityStatus.Missing,
        RuntimeRequirementStatus.UnsupportedSchema => RuntimeCapabilityStatus.UnsupportedSchema,
        // MissingActivityType belongs to the import gate's axis, which this endpoint does not
        // project (see CheckCapabilities). Reaching here means the checker emitted it on a
        // consumer or driver entry, which it never does — fail loudly rather than mis-map it.
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unexpected runtime requirement status on a consumer or storage-driver entry.")
    };

    private static IReadOnlyList<ActivityDiagnostic> BuildDiagnostics(IEnumerable<RuntimeArtifactPreflight> artifacts)
    {
        var diagnostics = new List<ActivityDiagnostic>();
        foreach (var artifact in artifacts.OrderBy(x => x.ArtifactId, StringComparer.Ordinal))
        {
            var subject = new ActivityDiagnosticSubject("WorkflowArtifact", artifact.ArtifactId);
            if (!artifact.IsRetained)
                diagnostics.Add(new(
                    "activity.preflight.artifact-not-retained",
                    ActivityDiagnosticSeverity.Error,
                    "The selected artifact has no active retained Source Reference.",
                    subject,
                    Remediation: "Select an active retained artifact or omit artifactIds to check the complete retained set.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));
            else if (!artifact.IsAvailable)
                diagnostics.Add(new(
                    "activity.preflight.artifact-missing",
                    ActivityDiagnosticSeverity.Error,
                    "An active retained Source Reference points to a missing artifact.",
                    subject,
                    Remediation: "Restore or republish the retained artifact before deployment.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)));

            // Activity-consumer failures previously produced no diagnostic at all: this loop was
            // hardcoded to DurableValueStorageDriver, so an artifact could report IsReady == false
            // with nothing in Diagnostics explaining which consumer was missing. Keys and message
            // shape mirror ActivityPublicationReviewPolicy so both surfaces speak one vocabulary.
            foreach (var capability in artifact.Capabilities.Where(x =>
                         x.Kind == RuntimeCapabilityKind.ActivityConsumer &&
                         x.Status != RuntimeCapabilityStatus.Available))
                diagnostics.Add(new(
                    capability.Status == RuntimeCapabilityStatus.UnsupportedSchema
                        ? "activity.runtime.consumer-schema-unsupported"
                        : "activity.runtime.consumer-missing",
                    ActivityDiagnosticSeverity.Error,
                    $"Required Runtime consumer '{capability.Key}' schema '{capability.SchemaVersion}' is unavailable.",
                    subject,
                    Remediation: "Deploy and register a Runtime consumer that supports the exact schema before deployment.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["consumerKey"] = capability.Key,
                        ["schemaVersion"] = capability.SchemaVersion ?? ""
                    }));

            foreach (var capability in artifact.Capabilities.Where(x =>
                         x.Kind == RuntimeCapabilityKind.DurableValueStorageDriver &&
                         x.Status != RuntimeCapabilityStatus.Available))
                diagnostics.Add(new(
                    "activity.runtime.storage-driver-missing",
                    ActivityDiagnosticSeverity.Error,
                    $"Required durable value storage driver '{capability.Key}' is unavailable.",
                    subject,
                    Remediation: "Install and register the required durable value storage driver before deployment.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["driverKey"] = capability.Key
                    }));
        }

        return ActivityDiagnosticOrderer.Order(diagnostics);
    }

    private static RuntimeCapabilityStatus WorstStatus(IEnumerable<RuntimeCapabilityStatus> statuses) =>
        statuses.Max();

    private sealed class StringTupleComparer : IEqualityComparer<(string Key, string? SchemaVersion)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Key, string? SchemaVersion) x, (string Key, string? SchemaVersion) y) =>
            StringComparer.Ordinal.Equals(x.Key, y.Key) && StringComparer.Ordinal.Equals(x.SchemaVersion, y.SchemaVersion);

        public int GetHashCode((string Key, string? SchemaVersion) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Key),
                obj.SchemaVersion is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.SchemaVersion));
    }
}
