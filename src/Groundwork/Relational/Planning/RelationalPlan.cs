using Groundwork.Core.Materialization;
using Groundwork.Core.Validation;

namespace Groundwork.Relational.Planning;

public sealed record RelationalPlan(
    IReadOnlyList<RelationalTablePlan> Tables,
    IReadOnlyList<MaterializationOperation> Operations,
    SchemaHistoryEntry SchemaHistory,
    IReadOnlyList<GroundworkDiagnostic> Diagnostics)
{
    public bool IsPlannable => Diagnostics.All(diagnostic => !diagnostic.IsError);
}

public sealed record RelationalTablePlan(
    string Name,
    IReadOnlyList<RelationalColumnPlan> Columns,
    IReadOnlyList<RelationalIndexPlan> Indexes);

public sealed record RelationalColumnPlan(string Name, string Role);

public sealed record RelationalIndexPlan(
    string Name,
    IReadOnlyList<string> Fields,
    bool IsUnique,
    bool IsSortable);
