using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Services;

/// <summary>Computes exact selection and describes supplied host evidence without loading or activating features.</summary>
public static class SelectionPlanner
{
    public static SelectionPlan Plan(
        SelectionCatalog catalog,
        AuthoredComposition authored,
        HostInventory? inventory = null,
        IReadOnlyCollection<WorkspaceProfile>? workspaceProfiles = null,
        PersistenceEvidence? persistence = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(authored);
        ValidateCatalog(catalog);
        if (authored.SchemaVersion != "1")
            throw new SelectionDocumentException("schema-unsupported", "The authored composition schema is unsupported.");
        ValidateAuthored(authored);

        var findings = ImmutableArray.CreateBuilder<SelectionFinding>();
        var reasons = ImmutableArray.CreateBuilder<SelectionReason>();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var selectedDefinitions = new List<SelectionDefinition>();
        var suppliedProfiles = workspaceProfiles ?? Array.Empty<WorkspaceProfile>();
        ValidateWorkspaceProfiles(suppliedProfiles);

        var catalogMatches = catalog.Id == authored.Catalog.Id && catalog.Version == authored.Catalog.Version && catalog.Digest == authored.Catalog.Digest;
        if (!catalogMatches)
            findings.Add(new SelectionFinding("catalog-pin-unresolved", "unresolved", null, null, "authored-catalog-pin", "The supplied catalog does not match the authored ID, version and digest."));
        if (authored.Accepted.CatalogDigest != authored.Catalog.Digest)
            findings.Add(new SelectionFinding("accepted-pin-unresolved", "unresolved", null, null, "accepted-lock", "The accepted expansion references a different catalog digest from authored intent."));

        if (authored.Profile is { } profile)
        {
            var definition = Resolve(profile, catalogMatches ? catalog.Profiles : [], suppliedProfiles, findings);
            if (definition is not null)
            {
                selectedDefinitions.Add(definition);
                AddMembers(definition, profile.Origin == "workspace" ? "workspace-profile" : "profile", selected, reasons);
            }
        }
        foreach (var group in authored.Groups)
        {
            if (group.Kind != "group" || group.Origin != "foundation")
                throw new SelectionDocumentException("invalid-field", "Only Foundation group references are supported in v1.");
            var definition = Resolve(group, catalogMatches ? catalog.Groups : [], suppliedProfiles, findings);
            if (definition is not null)
            {
                selectedDefinitions.Add(definition);
                AddMembers(definition, "group", selected, reasons);
            }
        }

        foreach (var featureId in authored.Add)
        {
            selected.Add(featureId);
            reasons.Add(new SelectionReason(featureId, "selected", "explicit-add", featureId, null, "Explicit authored addition."));
        }
        foreach (var featureId in authored.Remove)
        {
            selected.Remove(featureId);
            reasons.Add(new SelectionReason(featureId, "removed", "explicit-remove", featureId, null, "Explicit authored removal wins over all inclusion sources."));
        }

        var exact = selected.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray();
        var orderedReasons = reasons
            .OrderBy(x => x.FeatureId, StringComparer.Ordinal)
            .ThenBy(x => x.Action, StringComparer.Ordinal)
            .ThenBy(x => x.SourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .ThenBy(x => x.SourceVersion, StringComparer.Ordinal)
            .ThenBy(x => x.Rationale, StringComparer.Ordinal)
            .ToImmutableArray();

        if (!exact.SequenceEqual(authored.Accepted.FeatureIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            findings.Add(new SelectionFinding("candidate-re-resolution", "advisory", null, null, "accepted-lock", "The candidate exact selection differs from the previously accepted expansion."));

        var (hostFindings, locks, hostDependencies) = HostAssessment.Assess(exact, orderedReasons, inventory);
        findings.AddRange(hostFindings);
        var reviewedDependencies = selectedDefinitions
            .SelectMany(definition => definition.DependencyExplanations
                .Where(explanation => selected.Contains(explanation.FeatureId))
                .Select(explanation => new DependencyEvidence(
                    explanation.FeatureId,
                    explanation.DependencyId,
                    explanation.Mode,
                    "reviewed-definition",
                    selected.Contains(explanation.DependencyId)) { EvidenceSource = definition.Id }));
        foreach (var edge in reviewedDependencies
                     .Where(edge => edge.Mode == "required" && !edge.TargetSelected)
                     .DistinctBy(edge => (edge.FeatureId, edge.DependencyId)))
        {
            var observed = inventory?.Features.FirstOrDefault(row => row.FeatureId == edge.FeatureId);
            var hasRuntimeDependencies = observed is { RuntimeDependencies: not null };
            var hasReadableManifestDependencies = observed is { ManifestDependencies: not null, ManifestReadStatus: "read" };
            if (hasRuntimeDependencies || hasReadableManifestDependencies)
                continue;
            var removed = orderedReasons.Any(reason => reason.FeatureId == edge.DependencyId && reason.Action == "removed");
            findings.Add(new SelectionFinding(
                "required-dependency-missing", "unresolved", edge.FeatureId, edge.DependencyId, "reviewed-definition",
                removed ? "The reviewed required feature was explicitly removed and remains absent." :
                    "The reviewed required feature is absent from the exact selection."));
        }
        var dependencyEvidence = reviewedDependencies.Concat(hostDependencies)
            .Distinct()
            .OrderBy(edge => edge.FeatureId, StringComparer.Ordinal)
            .ThenBy(edge => edge.DependencyId, StringComparer.Ordinal)
            .ThenBy(edge => edge.Mode, StringComparer.Ordinal)
            .ThenBy(edge => edge.EvidenceKind, StringComparer.Ordinal)
            .ThenBy(edge => edge.EvidenceSource, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetSelected)
            .ToImmutableArray();
        var checkedPersistence = ValidatePersistence(persistence);
        if (checkedPersistence.Status != "checked")
            findings.Add(new SelectionFinding("persistence-unverified", "unresolved", null, null, checkedPersistence.Provenance, "Provider, connection, schema, migration and physical layout were not all verified by supplied persistence evidence."));

        var orderedFindings = findings
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.FeatureId, StringComparer.Ordinal)
            .ThenBy(x => x.DependencyId, StringComparer.Ordinal)
            .ThenBy(x => x.EvidenceSource, StringComparer.Ordinal)
            .ThenBy(x => x.Explanation, StringComparer.Ordinal)
            .ToImmutableArray();
        var accepted = authored.Accepted with
        {
            FeatureIds = authored.Accepted.FeatureIds.OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray(),
            Locks = authored.Accepted.Locks.OrderBy(x => x.FeatureId, StringComparer.Ordinal).ToImmutableArray()
        };
        return new SelectionPlan(
            authored.Catalog,
            inventory?.InventoryId,
            inventory?.TargetId,
            inventory?.ObservedAt,
            exact,
            orderedReasons,
            orderedFindings,
            locks.OrderBy(x => x.FeatureId, StringComparer.Ordinal).ToImmutableArray(),
            accepted,
            checkedPersistence)
        {
            DependencyEvidence = dependencyEvidence
        };
    }

    private static SelectionDefinition? Resolve(
        DefinitionReference reference,
        ImmutableArray<SelectionDefinition> foundation,
        IReadOnlyCollection<WorkspaceProfile> workspace,
        ImmutableArray<SelectionFinding>.Builder findings)
    {
        SelectionDefinition? definition = reference.Origin switch
        {
            "foundation" => foundation.FirstOrDefault(item => item.Kind == reference.Kind && item.Id == reference.Id && item.Version == reference.Version && item.Digest == reference.Digest),
            "workspace" when reference.Kind == "profile" => workspace.Select(item => item.Definition).FirstOrDefault(item => item.Kind == reference.Kind && item.Id == reference.Id && item.Version == reference.Version && item.Digest == reference.Digest),
            _ => throw new SelectionDocumentException("invalid-field", "Definition origin or kind is unsupported.")
        };
        if (definition is not null)
            return definition;
        findings.Add(new SelectionFinding("definition-pin-unresolved", "unresolved", null, null, reference.Origin, $"Definition {reference.Kind}:{reference.Id}@{reference.Version} does not match a supplied immutable snapshot."));
        return null;
    }

    private static void AddMembers(
        SelectionDefinition definition,
        string sourceKind,
        HashSet<string> selected,
        ImmutableArray<SelectionReason>.Builder reasons)
    {
        foreach (var featureId in definition.Members)
        {
            selected.Add(featureId);
            reasons.Add(new SelectionReason(featureId, "selected", sourceKind, definition.Id, definition.Version, definition.Rationale));
        }
    }

    private static void ValidateCatalog(SelectionCatalog catalog)
    {
        if (catalog.SchemaVersion != "1")
            throw new SelectionDocumentException("schema-unsupported", "The selection catalog schema is unsupported.");
        if (catalog.Profiles.IsDefault || catalog.Groups.IsDefault || string.IsNullOrWhiteSpace(catalog.Id) ||
            string.IsNullOrWhiteSpace(catalog.Version) || string.IsNullOrWhiteSpace(catalog.Publisher))
            throw new SelectionDocumentException("invalid-field", "The catalog identity and definition arrays are required.");
        var all = catalog.Profiles.Concat(catalog.Groups).ToArray();
        if (catalog.Profiles.Any(x => x.Kind != "profile") || catalog.Groups.Any(x => x.Kind != "group") ||
            all.GroupBy(x => x.Id, StringComparer.Ordinal).Any(group => group.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() > 1) ||
            all.GroupBy(x => (x.Kind, x.Id, x.Version)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("duplicate-definition", "Catalog definitions must have the expected kind and a unique ID/version.");
        foreach (var definition in catalog.Profiles.Concat(catalog.Groups))
            ValidateDefinition(definition);
        if (catalog.Digest != SelectionDigest.ComputeCatalogDigest(catalog))
            throw new SelectionDocumentException("digest-mismatch", "The supplied catalog changed after its digest was set.");
    }

    private static void ValidateWorkspaceProfiles(IReadOnlyCollection<WorkspaceProfile> profiles)
    {
        if (profiles.GroupBy(x => (x.Definition.Id, x.Definition.Version)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("duplicate-definition", "Workspace profile ID/version must be unique.");
        foreach (var profile in profiles)
        {
            if (profile.SchemaVersion != "1")
                throw new SelectionDocumentException("schema-unsupported", "The workspace profile schema is unsupported.");
            if (profile.Definition.Kind != "profile")
                throw new SelectionDocumentException("invalid-field", "A workspace definition must be a profile.");
            ValidateDefinition(profile.Definition);
        }
    }

    private static void ValidateDefinition(SelectionDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Version) ||
            string.IsNullOrWhiteSpace(definition.Rationale) || string.IsNullOrWhiteSpace(definition.Title) ||
            string.IsNullOrWhiteSpace(definition.Description) || definition.Members.IsDefault ||
            definition.DependencyExplanations.IsDefault || definition.Members.Any(string.IsNullOrWhiteSpace) ||
            definition.Members.Distinct(StringComparer.Ordinal).Count() != definition.Members.Length ||
            definition.DependencyExplanations.Any(x => x.Mode is not ("required" or "optional") ||
                string.IsNullOrWhiteSpace(x.Reason) || !definition.Members.Contains(x.FeatureId, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(x.DependencyId)) ||
            definition.DependencyExplanations.GroupBy(x => (x.FeatureId, x.DependencyId, x.Mode)).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("invalid-field", "A definition contains invalid or duplicate members or explanations.");
        if (definition.Digest != SelectionDigest.ComputeDefinitionDigest(definition))
            throw new SelectionDocumentException("digest-mismatch", $"Definition {definition.Id}@{definition.Version} changed after its digest was set.");
    }

    private static void ValidateAuthored(AuthoredComposition authored)
    {
        if (authored.Catalog is null || authored.Accepted is null)
            throw new SelectionDocumentException("invalid-field", "The authored selection requires catalog and accepted pins.");
        if (string.IsNullOrWhiteSpace(authored.Catalog.Id) || string.IsNullOrWhiteSpace(authored.Catalog.Version) ||
            !SelectionValueRules.IsDigest(authored.Catalog.Digest) || !SelectionValueRules.IsDigest(authored.Accepted.CatalogDigest) ||
            authored.Groups.IsDefault || authored.Add.IsDefault || authored.Remove.IsDefault ||
            authored.Accepted.FeatureIds.IsDefault || authored.Accepted.Locks.IsDefault ||
            authored.Profile is { } profile && (profile.Kind != "profile" || !ValidReference(profile)) ||
            authored.Groups.Any(x => x.Kind != "group" || x.Origin != "foundation" || !ValidReference(x)) ||
            authored.Groups.Distinct().Count() != authored.Groups.Length ||
            authored.Add.Any(string.IsNullOrWhiteSpace) || authored.Remove.Any(string.IsNullOrWhiteSpace) ||
            authored.Add.Distinct(StringComparer.Ordinal).Count() != authored.Add.Length ||
            authored.Remove.Distinct(StringComparer.Ordinal).Count() != authored.Remove.Length ||
            authored.Accepted.FeatureIds.Any(string.IsNullOrWhiteSpace) ||
            authored.Accepted.FeatureIds.Distinct(StringComparer.Ordinal).Count() != authored.Accepted.FeatureIds.Length ||
            authored.Accepted.Locks.GroupBy(x => x.FeatureId, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
            authored.Accepted.Locks.Any(x => !ValidLock(x, authored.Accepted.FeatureIds)))
            throw new SelectionDocumentException("invalid-field", "The authored selection contains invalid or duplicate IDs.");
    }

    private static bool ValidReference(DefinitionReference reference) =>
        reference.Origin is "foundation" or "workspace" && reference.Kind is "profile" or "group" &&
        !string.IsNullOrWhiteSpace(reference.Id) && !string.IsNullOrWhiteSpace(reference.Version) &&
        SelectionValueRules.IsDigest(reference.Digest);

    private static bool ValidLock(FeatureLock featureLock, ImmutableArray<string> acceptedIds) =>
        !string.IsNullOrWhiteSpace(featureLock.FeatureId) && acceptedIds.Contains(featureLock.FeatureId, StringComparer.Ordinal) &&
        SelectionValueRules.IsSafeReference(featureLock.EvidenceSource) &&
        (featureLock.Kind switch
        {
            "hostBundled" => featureLock.PackageId is null && featureLock.PackageVersion is null && featureLock.ManifestDigest is null,
            "package" => SelectionValueRules.IsSafeReference(featureLock.PackageId) &&
                SelectionValueRules.IsSafeReference(featureLock.PackageVersion) && SelectionValueRules.IsDigest(featureLock.ManifestDigest),
            _ => false
        });

    private static PersistenceEvidence ValidatePersistence(PersistenceEvidence? evidence)
    {
        if (evidence is null)
            return new PersistenceEvidence("unchecked", "none", [], []);
        if (evidence.Status is not ("checked" or "unchecked" or "unresolved") || !SelectionValueRules.IsSafeReference(evidence.Provenance) ||
            evidence.ResourceReferences.IsDefault || evidence.UnresolvedReasons.IsDefault ||
            evidence.ResourceReferences.Any(x => !SelectionValueRules.IsSafeReference(x)) ||
            evidence.UnresolvedReasons.Any(x => !SelectionValueRules.IsSafeReference(x)))
            throw new SelectionDocumentException("invalid-field", "The supplied persistence summary is invalid.");
        return evidence;
    }
}
