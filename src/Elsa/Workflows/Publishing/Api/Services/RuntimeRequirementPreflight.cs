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
    IEnumerable<IRuntimeActivityConsumerCapability> activityConsumers,
    IRuntimeDurableValueStorageDriverRegistry storageDrivers,
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
        var references = await sourceReferences.ListAsync(liveOnly: true, now: asOf, cancellationToken: cancellationToken);
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

            var (consumers, drivers, found) = await LoadRequirementsAsync(artifactId, cancellationToken);
            artifacts.Add(new(
                artifactId,
                true,
                found,
                found ? CheckCapabilities(consumers, drivers) : []));
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

    private async ValueTask<(IReadOnlyCollection<RuntimeRequirement> Consumers,
        IReadOnlyCollection<RuntimeStorageDriverRequirement> Drivers, bool Found)> LoadRequirementsAsync(
        string artifactId,
        CancellationToken cancellationToken)
    {
        var executable = await workflowExecutables.FindAsync(artifactId, cancellationToken);
        if (executable is not null)
            return (executable.RuntimeRequirements, executable.StorageDriverRequirements, true);

        var template = await activityTemplates.FindAsync(artifactId, cancellationToken);
        return template is null
            ? ([], [], false)
            : (template.RuntimeRequirements, template.StorageDriverRequirements, true);
    }

    private IReadOnlyList<RuntimeCapabilityPreflight> CheckCapabilities(
        IReadOnlyCollection<RuntimeRequirement> consumerRequirements,
        IReadOnlyCollection<RuntimeStorageDriverRequirement> storageDriverRequirements)
    {
        var result = new List<RuntimeCapabilityPreflight>();
        var consumersByKey = activityConsumers
            .GroupBy(capability => capability.ConsumerKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(capability => capability.SupportedSchemaVersions)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        foreach (var requirement in consumerRequirements
                     .Distinct()
                     .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
                     .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal))
        {
            IReadOnlyCollection<string> supported = consumersByKey.GetValueOrDefault(requirement.ConsumerKey) ?? [];
            var status = supported.Contains(requirement.SchemaVersion, StringComparer.Ordinal)
                ? RuntimeCapabilityStatus.Available
                : supported.Count == 0 ? RuntimeCapabilityStatus.Missing : RuntimeCapabilityStatus.UnsupportedSchema;
            result.Add(new(
                RuntimeCapabilityKind.ActivityConsumer,
                requirement.ConsumerKey,
                requirement.SchemaVersion,
                status,
                supported.Order(StringComparer.Ordinal).ToArray()));
        }

        foreach (var requirement in storageDriverRequirements.Distinct().OrderBy(x => x.DriverKey, StringComparer.Ordinal))
            result.Add(new(
                RuntimeCapabilityKind.DurableValueStorageDriver,
                requirement.DriverKey,
                null,
                storageDrivers.Contains(requirement.DriverKey) ? RuntimeCapabilityStatus.Available : RuntimeCapabilityStatus.Missing,
                []));

        return result;
    }

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
