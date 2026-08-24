using Elsa3.Activities.Design.Import.Models;
using Groundwork.Kernel;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork;

/// <summary>Fresh Groundwork v2 storage units owned by the Elsa 3 import boundary.</summary>
public static class Elsa3ImportStorageManifest
{
    public const string SchemaVersion = "1.0.0";
    public const int StorageSchemaVersion = 1;
    public const string CollectionDocumentKind = "elsa3ReusableImportCollection";
    public const string ReceiptDocumentKind = "elsa3ReusableImportReceipt";
    public const string DefinitionBindingDocumentKind = "elsa3ReusableImportDefinitionBinding";

    public const string IdField = "id";
    public const string SchemaVersionField = "schemaVersion";
    public const string ContentField = "content";
    public const string RevisionField = "revision";
    public const string UpdatedAtField = "updatedAt";
    public const string ScopeField = "scope";
    public const string TenantIdField = "tenantId";
    public const string SearchTextField = "searchText";

    public static IReadOnlyList<StorageUnit> CreateUnits() =>
        [
            CreateUnit(CollectionDocumentKind, "elsa3_reusable_import_collections"),
            CreateUnit(ReceiptDocumentKind, "elsa3_reusable_import_receipts"),
            CreateUnit(DefinitionBindingDocumentKind, "elsa3_reusable_import_definition_bindings")
        ];

    public static StorageUnit Require(string unitId) =>
        CreateUnits().Single(unit => StringComparer.Ordinal.Equals(unit.Id.Value, unitId));

    public static string ReceiptId(string idempotencyKey, ReusableActivityImportAccessScope accessScope) =>
        ReusableActivityImportIdentity.Create(
            "receipt",
            accessScope.TenantScope,
            accessScope.UserId,
            idempotencyKey);

    private static StorageUnit CreateUnit(string id, string name) =>
        StorageUnit.Declare(id, name)
            .String(IdField, 450, column => column.Required())
            .String(SchemaVersionField, 32, column => column.Required())
            .Json(ContentField, column => column.Required())
            .Int64(RevisionField, column => column.Required())
            .Timestamp(UpdatedAtField, column => column.Required())
            .String(ScopeField, 256)
            .String(TenantIdField, 256)
            .String(SearchTextField, 4000)
            .Key(IdField)
            .OptimisticConcurrency()
            .Scoped()
            .Build() with { SchemaVersion = StorageSchemaVersion };
}
