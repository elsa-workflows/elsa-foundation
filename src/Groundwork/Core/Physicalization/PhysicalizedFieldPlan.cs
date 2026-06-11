using Groundwork.Core.Indexing;

namespace Groundwork.Core.Physicalization;

public sealed record PhysicalizedFieldPlan(
    string Name,
    string Path,
    IndexValueKind ValueKind,
    bool IsUnique,
    bool IsSortable);
