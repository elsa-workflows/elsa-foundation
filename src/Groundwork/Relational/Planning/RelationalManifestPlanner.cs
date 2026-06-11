using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Materialization;
using Groundwork.Core.Validation;

namespace Groundwork.Relational.Planning;

public sealed class RelationalManifestPlanner(
    StorageManifestValidator manifestValidator,
    ProviderCapabilityValidator capabilityValidator)
{
    public RelationalPlan Plan(StorageManifest manifest, ProviderCapabilityReport capabilities)
    {
        var manifestValidation = manifestValidator.Validate(manifest);
        var compatibility = manifestValidation.IsValid
            ? capabilityValidator.Validate(manifest, capabilities)
            : CapabilityCompatibilityResult.Compatible;
        var diagnostics = manifestValidation.Diagnostics.Concat(compatibility.Diagnostics).ToList();

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new RelationalPlan(
                [],
                [],
                CreateHistory(manifest, capabilities, []),
                diagnostics);
        }

        var tables = manifest.StorageUnits
            .Select(unit => new RelationalTablePlan(
                unit.Identity.Value,
                CreateColumns(unit),
                unit.Indexes
                    .Select(index => new RelationalIndexPlan(
                        index.Identity,
                        index.Fields.Select(field => field.Path).ToList(),
                        index.IsUnique,
                        index.IsSortable))
                    .ToList()))
            .ToList();

        var operations = manifest.StorageUnits
            .SelectMany(unit => CreateOperations(unit))
            .Append(new MaterializationOperation(MaterializationOperationKind.RecordSchemaHistory, manifest.Identity.Value, new Dictionary<string, string>()))
            .ToList();

        return new RelationalPlan(
            tables,
            operations,
            CreateHistory(manifest, capabilities, operations),
            diagnostics);
    }

    private static IReadOnlyList<RelationalColumnPlan> CreateColumns(StorageUnit unit)
    {
        var columns = new List<RelationalColumnPlan>
        {
            new(unit.IdentityPolicy.FieldName, "Identity")
        };

        if (unit.Concurrency.TokenField is not null)
            columns.Add(new RelationalColumnPlan(unit.Concurrency.TokenField, "Concurrency"));

        if (unit.Tenancy.PartitionField is not null)
            columns.Add(new RelationalColumnPlan(unit.Tenancy.PartitionField, "Partition"));

        columns.AddRange(unit.Indexes.SelectMany(index => index.Fields).Select(field => new RelationalColumnPlan(field.Path, "Index")));
        return columns.DistinctBy(column => column.Name).ToList();
    }

    private static IEnumerable<MaterializationOperation> CreateOperations(StorageUnit unit)
    {
        yield return new MaterializationOperation(
            MaterializationOperationKind.CreateStorageUnit,
            unit.Identity.Value,
            new Dictionary<string, string> { ["shape"] = "relational-table" });

        foreach (var index in unit.Indexes)
        {
            yield return new MaterializationOperation(
                MaterializationOperationKind.CreateIndex,
                $"{unit.Identity.Value}.{index.Identity}",
                new Dictionary<string, string> { ["shape"] = "relational-index" });
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
