using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Core.Validation;

namespace Groundwork.Core.Capabilities;

public sealed class ProviderCapabilityValidator
{
    public CapabilityCompatibilityResult Validate(StorageManifest manifest, ProviderCapabilityReport capabilities)
    {
        var diagnostics = new List<GroundworkDiagnostic>();

        if (!capabilities.SupportsSchemaHistory)
            diagnostics.Add(GroundworkDiagnostic.Error("GW-CAP-001", "Provider must support schema history for materializable plans.", "provider.schemaHistory"));

        foreach (var warning in capabilities.Warnings)
            diagnostics.Add(GroundworkDiagnostic.Warning("GW-CAP-002", warning, "provider.warnings"));

        ValidateRequiredCapabilities(manifest, capabilities, diagnostics);

        foreach (var unit in manifest.StorageUnits)
            ValidateUnit(unit, capabilities, diagnostics);

        return diagnostics.Count == 0
            ? CapabilityCompatibilityResult.Compatible
            : new CapabilityCompatibilityResult(diagnostics);
    }

    private static void ValidateRequiredCapabilities(StorageManifest manifest, ProviderCapabilityReport capabilities, List<GroundworkDiagnostic> diagnostics)
    {
        foreach (var requiredCapability in manifest.RequiredCapabilities)
        {
            switch (requiredCapability)
            {
                case "schema-history":
                    break;
                case "optimistic-concurrency" when !capabilities.SupportedConcurrencyModes.Contains(ConcurrencyKind.Optimistic):
                    diagnostics.Add(GroundworkDiagnostic.Error(
                        "GW-CAP-005",
                        "Provider does not support required manifest capability 'optimistic-concurrency'.",
                        "requiredCapabilities.optimistic-concurrency"));
                    break;
                case "optimistic-concurrency":
                    break;
                default:
                    diagnostics.Add(GroundworkDiagnostic.Error(
                        "GW-CAP-012",
                        $"Manifest requires unknown provider capability '{requiredCapability}'.",
                        $"requiredCapabilities.{requiredCapability}"));
                    break;
            }
        }
    }

    private static void ValidateUnit(StorageUnit unit, ProviderCapabilityReport capabilities, List<GroundworkDiagnostic> diagnostics)
    {
        if (!capabilities.SupportedWorkloads.Contains(unit.Workload.Family))
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-003",
                $"Provider does not support workload family '{unit.Workload.Family}'.",
                $"storageUnits.{unit.Identity}.workload.family"));
        }

        if (!capabilities.SupportedCandidateCategories.Contains(unit.Workload.CandidateCategory))
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-004",
                $"Provider does not support workload candidate category '{unit.Workload.CandidateCategory}'.",
                $"storageUnits.{unit.Identity}.workload.candidateCategory"));
        }

        if (!capabilities.SupportedConcurrencyModes.Contains(unit.Concurrency.Kind))
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-005",
                $"Provider does not support concurrency mode '{unit.Concurrency.Kind}'.",
                $"storageUnits.{unit.Identity}.concurrency"));
        }

        ValidateMaterializationOperations(unit, capabilities, diagnostics);

        foreach (var index in unit.Indexes)
            ValidateIndex(unit, index, capabilities, diagnostics);
    }

    private static void ValidateMaterializationOperations(StorageUnit unit, ProviderCapabilityReport capabilities, List<GroundworkDiagnostic> diagnostics)
    {
        foreach (var operation in RequiredMaterializationOperations(unit))
        {
            if (capabilities.SupportedMaterializationOperations.Contains(operation))
                continue;

            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-011",
                $"Provider does not support materialization operation '{operation}' required by storage unit '{unit.Identity.Value}'.",
                $"storageUnits.{unit.Identity}.materialization"));
        }
    }

    private static IEnumerable<MaterializationOperationKind> RequiredMaterializationOperations(StorageUnit unit)
    {
        yield return MaterializationOperationKind.CreateStorageUnit;

        if (unit.Indexes.Count != 0)
            yield return MaterializationOperationKind.CreateIndex;

        if (PhysicalizationProjection.EligibleFields(unit).Count != 0)
            yield return MaterializationOperationKind.CreateOptimizedProjection;
    }

    private static void ValidateIndex(StorageUnit unit, IndexDeclaration index, ProviderCapabilityReport capabilities, List<GroundworkDiagnostic> diagnostics)
    {
        if (!capabilities.Indexes.SupportedValueKinds.Contains(index.ValueKind))
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-006",
                $"Provider does not support index value kind '{index.ValueKind}'.",
                $"storageUnits.{unit.Identity}.indexes.{index.Identity}.valueKind"));
        }

        if (index.IsUnique && !capabilities.Indexes.SupportsUniqueIndexes)
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-007",
                $"Provider does not support unique index '{index.Identity}'.",
                $"storageUnits.{unit.Identity}.indexes.{index.Identity}.unique"));
        }

        if (index.IsSortable && !capabilities.Indexes.SupportsSortableIndexes)
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-008",
                $"Provider does not support sortable index '{index.Identity}'.",
                $"storageUnits.{unit.Identity}.indexes.{index.Identity}.sortable"));
        }

        if (!capabilities.Indexes.SupportedMissingValueBehaviors.Contains(index.MissingValueBehavior))
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-009",
                $"Provider does not support missing value behavior '{index.MissingValueBehavior}'.",
                $"storageUnits.{unit.Identity}.indexes.{index.Identity}.missingValueBehavior"));
        }

        var unsupportedOperations = index.SupportedOperations.Except(capabilities.SupportedQueryOperations).ToList();
        if (unsupportedOperations.Count != 0)
        {
            diagnostics.Add(GroundworkDiagnostic.Error(
                "GW-CAP-010",
                $"Provider does not support query operations required by index '{index.Identity}': {string.Join(", ", unsupportedOperations)}.",
                $"storageUnits.{unit.Identity}.indexes.{index.Identity}.operations"));
        }
    }
}
