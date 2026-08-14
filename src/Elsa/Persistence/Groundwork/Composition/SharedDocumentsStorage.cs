using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// The shared Groundwork document table used by Elsa's portable-document storage units, plus the
/// physical-storage recipe those units share. The recipe reproduces, frozen at migration time, the
/// layout the legacy physicalization bridge derived for these units — including the 450-character
/// bound on projected strings that SQL Server's index key budget requires — so declared storage
/// stays identical to the historical schema.
/// </summary>
public static class SharedDocumentsStorage
{
    public const string LogicalName = "groundwork_documents";
    public const string CanonicalJsonColumnName = "content_json";
    public const int StringProjectionLength = 450;

    public static readonly SharedStorageBinding Binding = new(LogicalName);
    public static readonly SharedDocumentStorageDefinition Definition = new(
        Binding,
        LogicalName,
        new DocumentEnvelopeDefinition(CanonicalJsonColumn: CanonicalJsonColumnName));

    // The scope column carries the envelope's default name: the bridge never renamed it, so the
    // frozen recipe must not either.
    private static readonly string StorageScopeColumnName = new DocumentEnvelopeDefinition().StorageScopeColumn;

    /// <summary>
    /// Declares a unit's physical storage in the shared document table. <paramref name="tenancy"/>
    /// must be the same policy the owning <see cref="StorageUnit"/> declares — it decides whether
    /// physical indexes are prefixed with the storage-scope column. Indexes marked
    /// <see cref="SharedDocumentsIndex.Projected"/> must satisfy the frozen bridge eligibility rule
    /// (single field, <see cref="MissingValueBehavior.Excluded"/>); the recipe throws otherwise
    /// rather than silently changing the historical schema.
    /// </summary>
    public static StorageUnitPhysicalStorage Create(
        string unitIdentity,
        TenancyPolicy tenancy,
        IReadOnlyList<SharedDocumentsIndex> indexes,
        IReadOnlyList<BoundedQueryDeclaration> queries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitIdentity);
        ArgumentNullException.ThrowIfNull(tenancy);
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentNullException.ThrowIfNull(queries);

        var projectedColumns = indexes
            .Where(index => index.Projected)
            .Select(index => ProjectedColumn(unitIdentity, index.Logical))
            .ToArray();
        var physicalIndexes = indexes
            .Where(index => index.Projected)
            .Select(index => new PhysicalIndexDefinition(
                index.Logical.Identity,
                PhysicalIndexColumns(tenancy, index.Logical.Identity),
                index.Logical.IsUnique,
                missingValueBehavior: index.Logical.MissingValueBehavior))
            .ToArray();
        var definition = PhysicalTableDefinition.SharedDocuments(
            Binding,
            projectedColumns,
            physicalIndexes,
            linkedProjectionLogicalName: projectedColumns.Length == 0
                ? null
                : $"{unitIdentity}_projection");

        return new StorageUnitPhysicalStorage(
            StorageUnitProvisioningMode.Dynamic,
            PhysicalStoragePolicy.Explicit(definition),
            indexes.Select(index => index.Logical).ToArray(),
            queries);
    }

    private static ProjectedColumnDefinition ProjectedColumn(string unitIdentity, LogicalIndexDeclaration index)
    {
        // Projection null-ness stands in for MissingValueBehavior.Excluded: an absent canonical path
        // becomes a null projection value instead of rejecting the document write.
        if (index.Fields.Count != 1 || index.MissingValueBehavior != MissingValueBehavior.Excluded)
        {
            throw new InvalidOperationException(
                $"Index '{index.Identity}' of storage unit '{unitIdentity}' cannot be projected: shared-documents " +
                "projection requires a single-field index with MissingValueBehavior.Excluded.");
        }

        var type = ToPortableType(index.GetValueKind(index.Fields[0]));
        return new ProjectedColumnDefinition(
            index.Identity,
            index.Fields[0].Path,
            type,
            type == PortablePhysicalType.String ? StringProjectionLength : null);
    }

    private static IReadOnlyList<PhysicalIndexColumnDefinition> PhysicalIndexColumns(TenancyPolicy tenancy, string columnName)
    {
        var columns = new List<PhysicalIndexColumnDefinition>();
        if (tenancy.Kind == TenancyKind.Scoped)
            columns.Add(new PhysicalIndexColumnDefinition(StorageScopeColumnName, columns.Count));
        columns.Add(new PhysicalIndexColumnDefinition(columnName, columns.Count));
        return columns;
    }

    private static PortablePhysicalType ToPortableType(IndexValueKind kind) => kind switch
    {
        IndexValueKind.String or IndexValueKind.Keyword => PortablePhysicalType.String,
        IndexValueKind.Number => PortablePhysicalType.Decimal,
        IndexValueKind.Boolean => PortablePhysicalType.Boolean,
        IndexValueKind.DateTime => PortablePhysicalType.DateTime,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

/// <summary>
/// One logical index of a shared-documents storage unit, together with the frozen decision of
/// whether it is projected into the unit's linked projection table. The flag captures the legacy
/// bridge's eligibility rule (single-field, MissingValueBehavior.Excluded, equality-capable) whose
/// inputs no longer exist on the declared surface.
/// </summary>
public sealed record SharedDocumentsIndex(LogicalIndexDeclaration Logical, bool Projected);
