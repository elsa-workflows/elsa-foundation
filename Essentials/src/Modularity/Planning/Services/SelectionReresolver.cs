using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Services;

/// <summary>Compares two explicitly supplied immutable snapshots without accepting either candidate.</summary>
public static class SelectionReresolver
{
    public static ReresolutionDiff Compare(SelectionInputs oldInputs, SelectionInputs candidateInputs)
    {
        ArgumentNullException.ThrowIfNull(oldInputs);
        ArgumentNullException.ThrowIfNull(candidateInputs);
        var oldPlan = SelectionPlanner.Plan(oldInputs.Catalog, oldInputs.Authored, oldInputs.Inventory, oldInputs.WorkspaceProfiles, oldInputs.Persistence);
        var candidatePlan = SelectionPlanner.Plan(candidateInputs.Catalog, candidateInputs.Authored, candidateInputs.Inventory, candidateInputs.WorkspaceProfiles, candidateInputs.Persistence);
        RejectReusedVersions(oldInputs.Catalog, candidateInputs.Catalog);
        RejectReusedWorkspaceVersions(oldInputs.WorkspaceProfiles, candidateInputs.WorkspaceProfiles);

        return new ReresolutionDiff(
            oldPlan,
            candidatePlan,
            candidatePlan.SelectedFeatureIds.Except(oldPlan.SelectedFeatureIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray(),
            oldPlan.SelectedFeatureIds.Except(candidatePlan.SelectedFeatureIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToImmutableArray(),
            Differences(oldPlan.Reasons, candidatePlan.Reasons),
            Differences(SelectedExplanations(oldInputs), SelectedExplanations(candidateInputs)),
            Differences(DependencyFindings(oldPlan), DependencyFindings(candidatePlan)),
            Differences(oldPlan.ObservedLocks, candidatePlan.ObservedLocks),
            ResourceImpact(oldPlan.Persistence, candidatePlan.Persistence));
    }

    private static void RejectReusedVersions(SelectionCatalog oldCatalog, SelectionCatalog candidateCatalog)
    {
        if (oldCatalog.Id == candidateCatalog.Id && oldCatalog.Version == candidateCatalog.Version && oldCatalog.Digest != candidateCatalog.Digest)
            throw new SelectionDocumentException("immutable-version-conflict", "A catalog ID/version was reused with changed content.");
        var previous = oldCatalog.Profiles.Concat(oldCatalog.Groups).ToDictionary(x => (x.Kind, x.Id, x.Version));
        foreach (var definition in candidateCatalog.Profiles.Concat(candidateCatalog.Groups))
            if (previous.TryGetValue((definition.Kind, definition.Id, definition.Version), out var old) && old.Digest != definition.Digest)
                throw new SelectionDocumentException("immutable-version-conflict", $"Definition {definition.Id}@{definition.Version} was changed in place.");
    }

    private static void RejectReusedWorkspaceVersions(IReadOnlyCollection<WorkspaceProfile>? oldProfiles, IReadOnlyCollection<WorkspaceProfile>? candidateProfiles)
    {
        var previous = (oldProfiles ?? Array.Empty<WorkspaceProfile>()).ToDictionary(x => (x.Definition.Kind, x.Definition.Id, x.Definition.Version));
        foreach (var profile in candidateProfiles ?? Array.Empty<WorkspaceProfile>())
            if (previous.TryGetValue((profile.Definition.Kind, profile.Definition.Id, profile.Definition.Version), out var old) && old.Definition.Digest != profile.Definition.Digest)
                throw new SelectionDocumentException("immutable-version-conflict", $"Workspace profile {profile.Definition.Id}@{profile.Definition.Version} was changed in place.");
    }

    private static ImmutableArray<DependencyExplanation> SelectedExplanations(SelectionInputs inputs)
    {
        var references = new List<DefinitionReference>();
        if (inputs.Authored.Profile is { } profile)
            references.Add(profile);
        references.AddRange(inputs.Authored.Groups);
        var all = inputs.Catalog.Profiles.Concat(inputs.Catalog.Groups)
            .Select(definition => (Origin: "foundation", Definition: definition))
            .Concat((inputs.WorkspaceProfiles ?? Array.Empty<WorkspaceProfile>()).Select(profile => (Origin: "workspace", Definition: profile.Definition)));
        return all
            .Where(pair => references.Any(reference => reference.Origin == pair.Origin && reference.Kind == pair.Definition.Kind && reference.Id == pair.Definition.Id && reference.Version == pair.Definition.Version && reference.Digest == pair.Definition.Digest))
            .SelectMany(pair => pair.Definition.DependencyExplanations)
            .OrderBy(x => x.FeatureId, StringComparer.Ordinal)
            .ThenBy(x => x.DependencyId, StringComparer.Ordinal)
            .ThenBy(x => x.Mode, StringComparer.Ordinal)
            .ThenBy(x => x.Reason, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<SelectionFinding> DependencyFindings(SelectionPlan plan) => plan.Findings
        .Where(finding => finding.Code is "required-dependency-missing" or "optional-companion" or "descriptor-manifest-divergence" or "dependency-evidence-unavailable")
        .ToImmutableArray();

    private static ImmutableArray<SelectionDifference<T>> Differences<T>(ImmutableArray<T> oldValues, ImmutableArray<T> candidateValues) where T : class
    {
        var changes = oldValues.Except(candidateValues).Select(value => new SelectionDifference<T>(value, null))
            .Concat(candidateValues.Except(oldValues).Select(value => new SelectionDifference<T>(null, value)));
        return changes.OrderBy(x => x.Old?.ToString() ?? x.Candidate?.ToString(), StringComparer.Ordinal).ToImmutableArray();
    }

    private static string ResourceImpact(PersistenceEvidence oldEvidence, PersistenceEvidence candidateEvidence)
    {
        if (!oldEvidence.ResourceReferences.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(candidateEvidence.ResourceReferences.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            return "resource-reference-changed";
        if (oldEvidence.Status != candidateEvidence.Status || oldEvidence.Provenance != candidateEvidence.Provenance ||
            !oldEvidence.UnresolvedReasons.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(candidateEvidence.UnresolvedReasons.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            return "persistence-evidence-changed";
        return "unverified";
    }
}
