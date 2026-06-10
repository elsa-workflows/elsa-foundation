using Groundwork.Core.Indexing;
using Groundwork.Core.Materialization;
using Groundwork.Core.Validation;

namespace Groundwork.Documents.Planning;

public sealed record DocumentPlan(
    IReadOnlyList<DocumentStoragePlan> Documents,
    IReadOnlyList<MaterializationOperation> Operations,
    SchemaHistoryEntry SchemaHistory,
    IReadOnlyList<GroundworkDiagnostic> Diagnostics)
{
    public bool IsPlannable => Diagnostics.All(diagnostic => !diagnostic.IsError);
}

public sealed record DocumentStoragePlan(
    string DocumentKind,
    DocumentEnvelopePlan Envelope,
    IReadOnlyList<DocumentIndexPlan> Indexes,
    IReadOnlyList<DocumentQueryPlan> Queries);

public sealed record DocumentEnvelopePlan(
    string IdentityField,
    string? ConcurrencyField,
    string? PartitionField,
    string? SchemaField);

public sealed record DocumentIndexPlan(
    string Name,
    IReadOnlyList<string> Fields,
    bool IsUnique,
    bool IsSortable);

public sealed record DocumentQueryPlan(
    string Name,
    string IndexName,
    IReadOnlyList<PortableQueryOperation> Operations);
