using Groundwork.Core.Indexing;

namespace Elsa.Persistence.Groundwork.RuntimeEntities;

public sealed record RuntimeEntityDefinition(
    string EntityType,
    string DisplayName,
    IReadOnlyList<RuntimeEntityIndex> Indexes,
    IReadOnlyList<RuntimeEntityField>? Fields = null);

public sealed record RuntimeEntityField(string Name, IndexValueKind ValueKind);

public sealed record RuntimeEntityIndex(
    string Name,
    string Path,
    IndexValueKind ValueKind,
    bool IsUnique = false,
    bool IsSortable = true);
