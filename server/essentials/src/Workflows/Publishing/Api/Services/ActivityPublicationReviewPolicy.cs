using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Builds the stable, server-authoritative evidence that binds a publication review to one exact
/// draft/head state. Keeping this policy separate from the publisher makes evidence ordering,
/// readiness, version selection, and token construction independently reviewable.
/// </summary>
internal sealed class ActivityPublicationReviewPolicy(
    IEnumerable<IActivityActivationStrategy>? activityActivationStrategies,
    IRuntimeDurableValueStorageDriverRegistry? storageDrivers,
    IActivityPublicationReadiness? readiness = null,
    IActivityPublicationVersionPolicy? versions = null)
{
    internal IActivityPublicationReadiness CandidateReadiness { get; } = readiness ?? new ActivityPublicationReadiness(activityActivationStrategies, storageDrivers);
    private readonly IActivityPublicationVersionPolicy versions = versions ?? new ActivityPublicationVersions();

    public IReadOnlyList<ActivityPublicationCapabilityReadinessView> StorageReadiness(ExecutableActivityTemplate? template) =>
        CandidateReadiness.StorageReadiness(template?.StorageDriverRequirements ?? []).Select(View).ToArray();

    public IReadOnlyList<ActivityPublicationCapabilityReadinessView> RuntimeReadiness(ExecutableActivityTemplate? template) =>
        CandidateReadiness.RuntimeReadiness(template?.RuntimeRequirements ?? []).Select(View).ToArray();

    public IEnumerable<ActivityDiagnostic> ReadinessDiagnostics(ActivityDiagnosticSubject subject, ExecutableActivityTemplate? template) =>
        CandidateReadiness.ReadinessDiagnostics(subject, template?.RuntimeRequirements ?? [], template?.StorageDriverRequirements ?? []);

    private static ActivityPublicationCapabilityReadinessView View(Elsa.Workflows.Publishing.Core.Models.PublicationCapabilityReadiness value) =>
        new(value.Kind, value.Key, value.SchemaVersion, value.Status, value.SupportedSchemaVersions);

    public static int ImpactRank(ActivityVersionChange change) => change.Impact switch
    {
        ActivityVersionChangeImpact.Breaking => 0,
        ActivityVersionChangeImpact.Additive => 1,
        _ => 2
    };

    public static string ProvisionalVersion(string? headVersion)
    {
        if (headVersion is null)
            return "1.0.0";
        var head = ParseHeadVersion(headVersion);
        return $"{head.Major}.{head.Minor}.{checked(head.Patch + 1)}";
    }

    public IReadOnlyList<string> AvailableVersionChoices(
        string? headVersion,
        ActivityVersionBump requiredBump,
        IReadOnlyCollection<ActivityDefinitionVersionPublication> existingVersions)
    {
        var existing = new List<SemVer>();
        foreach (var publication in existingVersions)
            if (SemVer.TryParse(publication.Version, out var version))
                existing.Add(version);
        return ValidVersionChoices(headVersion, requiredBump)
            .Where(choice =>
                SemVer.TryParse(choice, out var candidate) &&
                existing.All(version => version != candidate))
            .ToArray();
    }

    public string MinimumVersion(string? headVersion, ActivityVersionBump requiredBump) =>
        ValidVersionChoices(headVersion, requiredBump)[0];

    public static string StableCandidateVersionId(string definitionId, string draftId, long revision)
    {
        var value = $"{definitionId}\n{draftId}\n{revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return $"activity-ver-candidate-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";
    }

    public static bool IsVersionAtLeast(string candidateText, string minimumText) =>
        SemVer.TryParse(candidateText, out var candidate) &&
        SemVer.TryParse(minimumText, out var minimum) &&
        candidate >= minimum;

    public static string ReviewToken(
        ActivityDefinitionDraft draft,
        string? headVersionId,
        string reviewedVersion,
        ExecutableActivityTemplate? template,
        ActivityVersionDiff? diff,
        ActivityVersionBump requiredBump,
        IReadOnlyList<string> validVersions,
        IReadOnlyList<ActivityPublicationDependencyEvidenceView> dependencies,
        ActivityPublicationCapabilityReadinessView provider,
        IReadOnlyList<ActivityPublicationCapabilityReadinessView> storage,
        IReadOnlyList<ActivityPublicationCapabilityReadinessView> runtime,
        IReadOnlyList<ActivityDiagnostic> diagnostics)
    {
        var material = JsonSerializer.SerializeToUtf8Bytes(new
        {
            draft.Id,
            draft.TenantId,
            draft.DefinitionId,
            draft.Revision,
            HeadVersionId = headVersionId,
            ReviewedVersion = reviewedVersion,
            TemplateHash = template?.TemplateHash,
            template?.ProviderFingerprint,
            Diff = diff is null
                ? null
                : new
                {
                    diff.Compatibility,
                    diff.RequiredBump,
                    diff.BehaviorChanged,
                    diff.Provider,
                    diff.Summary,
                    diff.Changes,
                    diff.Diagnostics
                },
            RequiredBump = requiredBump.ToString(),
            ValidVersions = validVersions,
            Dependencies = dependencies,
            Provider = provider,
            Storage = storage,
            Runtime = runtime,
            Diagnostics = diagnostics.Select(x => new
            {
                x.Code,
                Severity = x.Severity.ToString(),
                x.Message,
                x.Remediation,
                x.Subject.Kind,
                x.Subject.Id,
                x.Subject.DefinitionId,
                x.Subject.VersionId,
                x.Subject.Revision,
                JsonPointer = x.Location?.JsonPointer,
                x.Location?.ProviderKey,
                x.Location?.ReferenceKey,
                NodeOrigin = x.Location?.NodeOrigin?.Select(y => new
                {
                    Kind = y.Kind.ToString(),
                    y.Id
                }),
                DependencyPath = x.Location?.DependencyPath?.Select(y => new
                {
                    y.DefinitionId,
                    y.VersionId,
                    y.Version,
                    y.TemplateHash
                }),
                Metadata = x.Metadata?.OrderBy(y => y.Key, StringComparer.Ordinal)
            })
        });
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(material))}";
    }

    private IReadOnlyList<string> ValidVersionChoices(string? headVersion, ActivityVersionBump requiredBump)
    {
        if (headVersion is not null) _ = ParseHeadVersion(headVersion);
        return versions.Choices(headVersion, requiredBump);
    }

    private static SemVer ParseHeadVersion(string headVersion)
    {
        if (SemVer.TryParse(headVersion, out var head))
            return head;
        throw new ActivityPublicationRejectedException(
            "activity.definition.head-invalid",
            "The current definition head has an invalid semantic version.",
            [],
            true);
    }
}
