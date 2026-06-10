using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Materialization;
using Groundwork.Core.Physicalization;
using Groundwork.Core.Validation;

namespace Groundwork.Documents.Planning;

public sealed class DocumentManifestPlanner(
    StorageManifestValidator manifestValidator,
    ProviderCapabilityValidator capabilityValidator)
{
    public DocumentPlan Plan(StorageManifest manifest, ProviderCapabilityReport capabilities)
    {
        var manifestValidation = manifestValidator.Validate(manifest);
        var compatibility = manifestValidation.IsValid
            ? capabilityValidator.Validate(manifest, capabilities)
            : CapabilityCompatibilityResult.Compatible;
        var diagnostics = manifestValidation.Diagnostics.Concat(compatibility.Diagnostics).ToList();

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new DocumentPlan(
                [],
                [],
                CreateHistory(manifest, capabilities, []),
                diagnostics);
        }

        var documents = manifest.StorageUnits
            .Select(unit => new DocumentStoragePlan(
                unit.Identity.Value,
                new DocumentEnvelopePlan(
                    unit.IdentityPolicy.FieldName,
                    unit.Concurrency.TokenField,
                    unit.Tenancy.PartitionField,
                    unit.Serialization.SchemaField),
                unit.Indexes
                    .Select(index => new DocumentIndexPlan(
                        index.Identity,
                        index.Fields.Select(field => field.Path).ToList(),
                        index.IsUnique,
                        index.IsSortable))
                    .ToList(),
                unit.Queries
                    .Select(query => new DocumentQueryPlan(
                        query.Identity,
                        query.IndexIdentity,
                        query.Operations.ToList()))
                    .ToList()))
            .ToList();

        var operations = manifest.StorageUnits
            .SelectMany(unit => CreateOperations(unit))
            .Append(new MaterializationOperation(MaterializationOperationKind.RecordSchemaHistory, manifest.Identity.Value, new Dictionary<string, string>()))
            .ToList();

        return new DocumentPlan(
            documents,
            operations,
            CreateHistory(manifest, capabilities, operations),
            diagnostics);
    }

    private static IEnumerable<MaterializationOperation> CreateOperations(StorageUnit unit)
    {
        yield return new MaterializationOperation(
            MaterializationOperationKind.CreateStorageUnit,
            unit.Identity.Value,
            new Dictionary<string, string> { ["shape"] = "document-envelope" });

        foreach (var index in unit.Indexes)
        {
            yield return new MaterializationOperation(
                MaterializationOperationKind.CreateIndex,
                $"{unit.Identity.Value}.{index.Identity}",
                new Dictionary<string, string> { ["shape"] = "document-index" });
        }

        foreach (var field in PhysicalizationProjection.EligibleFields(unit))
        {
            yield return new MaterializationOperation(
                MaterializationOperationKind.CreateOptimizedProjection,
                $"{unit.Identity.Value}.{field.Name}",
                new Dictionary<string, string>
                {
                    ["shape"] = "optimized-projection",
                    ["path"] = field.Path,
                    ["unique"] = field.IsUnique.ToString()
                });
        }
    }

    private static SchemaHistoryEntry CreateHistory(
        StorageManifest manifest,
        ProviderCapabilityReport capabilities,
        IReadOnlyList<MaterializationOperation> operations) =>
        new(
            manifest.Identity,
            manifest.Version,
            capabilities.Provider,
            operations.Select(operation => operation.Target).ToList());
}
