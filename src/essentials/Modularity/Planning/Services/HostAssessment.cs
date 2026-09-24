using System.Collections.Immutable;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Services;

internal static class HostAssessment
{
    public static (ImmutableArray<SelectionFinding> Findings, ImmutableArray<FeatureLock> Locks) Assess(
        ImmutableArray<string> selected,
        ImmutableArray<SelectionReason> reasons,
        HostInventory? inventory)
    {
        if (inventory is null)
            return ([new SelectionFinding("inventory-unverified", "unresolved", null, null, "none", "No target-host inventory was supplied.")], []);

        Validate(inventory);
        var rows = inventory.Features.ToDictionary(x => x.FeatureId, StringComparer.Ordinal);
        var set = selected.ToHashSet(StringComparer.Ordinal);
        var findings = ImmutableArray.CreateBuilder<SelectionFinding>();
        var locks = ImmutableArray.CreateBuilder<FeatureLock>();

        foreach (var featureId in selected)
        {
            if (!rows.TryGetValue(featureId, out var row))
            {
                findings.Add(Finding("feature-unknown", featureId, null, inventory.Source, "The target inventory has no observation for this feature ID."));
                continue;
            }

            if (row.Availability == "absent")
                findings.Add(Finding(row.Package is null ? "feature-unknown" : "package-absent", featureId, null, row.EvidenceSource, "The selected feature was not observed on this target."));
            else if (row.Availability == "unknown")
                findings.Add(Finding("feature-unknown", featureId, null, row.EvidenceSource, "Feature availability is unknown on this target."));

            if (row.ManifestReadStatus == "unreadable")
                findings.Add(Finding("manifest-unreadable", featureId, null, row.EvidenceSource, "The installed manifest could not be read."));

            if (!row.HostBundled)
            {
                if (row.Package is null)
                    findings.Add(Finding("package-identity-missing", featureId, null, row.EvidenceSource, "Neither an observed package lock nor a host-bundled marker was supplied."));
                else
                {
                    locks.Add(new FeatureLock(featureId, "package", row.Package.PackageId, row.Package.PackageVersion, row.Package.ManifestDigest, row.EvidenceSource));
                    if (row.Compatibility == "incompatible")
                        findings.Add(Finding("package-incompatible", featureId, null, row.EvidenceSource, "The observed package is incompatible with this target."));
                    else if (row.Compatibility == "unknown")
                        findings.Add(Finding("compatibility-unknown", featureId, null, row.EvidenceSource, "Package compatibility has not been established."));
                }
            }
            else
                locks.Add(new FeatureLock(featureId, "hostBundled", null, null, null, row.EvidenceSource));

            var runtimeEdges = row.RuntimeDependencies;
            var manifestEdges = row.ManifestDependencies;
            if (runtimeEdges is not null && manifestEdges is not null &&
                (!runtimeEdges.Value.OrderBy(x => x, StringComparer.Ordinal)
                    .SequenceEqual(manifestEdges.Value.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) ||
                 manifestEdges.Value.Any(x => x.Optional)))
                findings.Add(new SelectionFinding("descriptor-manifest-divergence", "advisory", featureId, null, row.EvidenceSource, "Runtime and manifest dependency lists disagree; the loaded descriptor governs."));

            if (runtimeEdges is not null)
            {
                foreach (var dependencyId in runtimeEdges.Value)
                    CheckRequired(dependencyId, "runtime-descriptor", featureId, set, reasons, findings);
            }
            else if (manifestEdges is not null && row.ManifestReadStatus == "read")
            {
                foreach (var edge in manifestEdges.Value)
                {
                    if (edge.Optional)
                    {
                        if (!set.Contains(edge.Id))
                            findings.Add(new SelectionFinding("optional-companion", "advisory", featureId, edge.Id, "package-manifest", "An optional companion is available as a separate explicit choice."));
                    }
                    else
                        CheckRequired(edge.Id, "package-manifest", featureId, set, reasons, findings);
                }
            }
            else
                findings.Add(Finding("dependency-evidence-unavailable", featureId, null, row.EvidenceSource, "No readable dependency evidence was supplied for this feature."));
        }

        return (findings.ToImmutable(), locks.ToImmutable());
    }

    private static void CheckRequired(
        string dependencyId,
        string source,
        string featureId,
        HashSet<string> selected,
        ImmutableArray<SelectionReason> reasons,
        ImmutableArray<SelectionFinding>.Builder findings)
    {
        if (selected.Contains(dependencyId))
            return;
        var removed = reasons.Any(reason => reason.FeatureId == dependencyId && reason.Action == "removed");
        findings.Add(new SelectionFinding(
            "required-dependency-missing", "unresolved", featureId, dependencyId, source,
            removed ? "The required feature was explicitly removed and remains absent." : "The required feature is absent from the exact selection."));
    }

    private static SelectionFinding Finding(string code, string? featureId, string? dependencyId, string source, string explanation) =>
        new(code, "unresolved", featureId, dependencyId, source, explanation);

    private static void Validate(HostInventory inventory)
    {
        if (!SelectionValueRules.IsSafeReference(inventory.InventoryId) ||
            !SelectionValueRules.IsSafeReference(inventory.TargetId) ||
            !SelectionValueRules.IsSafeReference(inventory.Source) || inventory.ObservedAt == default)
            throw new SelectionDocumentException("invalid-field", "Inventory identity, target, observation time and source are required.");
        if (inventory.Features.IsDefault || inventory.Features.GroupBy(x => x.FeatureId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new SelectionDocumentException("invalid-field", "Inventory feature rows must be present and unique.");
        foreach (var row in inventory.Features)
        {
            if (string.IsNullOrWhiteSpace(row.FeatureId) || !SelectionValueRules.IsSafeReference(row.EvidenceSource) ||
                row.Availability is not ("loaded" or "installed" or "absent" or "unknown") ||
                row.ManifestReadStatus is not ("read" or "unreadable" or "absent" or "unknown") ||
                row.Compatibility is not ("compatible" or "incompatible" or "unknown"))
                throw new SelectionDocumentException("invalid-field", "An inventory feature row has invalid evidence fields.");
            if (row.HostBundled && row.Package is not null)
                throw new SelectionDocumentException("invalid-field", "A host-bundled feature cannot also have a package identity.");
            if (row.RuntimeDependencies is not null && row.Availability != "loaded")
                throw new SelectionDocumentException("invalid-field", "Runtime descriptor edges require a loaded feature observation.");
            if (row.Package is { } package && (!SelectionValueRules.IsSafeReference(package.PackageId) ||
                !SelectionValueRules.IsSafeReference(package.PackageVersion) || !SelectionValueRules.IsDigest(package.ManifestDigest)))
                throw new SelectionDocumentException("invalid-field", "An observed package needs its ID, version and manifest digest.");
            if (row.RuntimeDependencies is { } runtime && (runtime.IsDefault || runtime.Any(string.IsNullOrWhiteSpace) || runtime.Distinct(StringComparer.Ordinal).Count() != runtime.Length))
                throw new SelectionDocumentException("invalid-field", "Runtime dependency IDs must be unique and nonempty.");
            if (row.ManifestDependencies is { } manifest && (manifest.IsDefault || manifest.Any(edge => string.IsNullOrWhiteSpace(edge.Id)) || manifest.GroupBy(edge => edge.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)))
                throw new SelectionDocumentException("invalid-field", "Manifest dependency IDs must be unique and nonempty.");
        }
    }
}
