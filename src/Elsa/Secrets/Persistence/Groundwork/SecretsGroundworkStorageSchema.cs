using Elsa.Secrets.Core.Models;
using Groundwork.Kernel;

namespace Elsa.Secrets.Persistence.Groundwork;

/// <summary>The fresh, ordinary Groundwork v2 row owned by the Secrets feature.</summary>
public static class SecretsGroundworkStorageSchema
{
    public const string UnitId = "elsa-secrets";
    public const string UnitName = "elsa_secrets";
    public const string TenantIdField = "tenantId";
    public const string NormalizedNameField = "normalizedName";
    public const string NameSearchKeyField = "nameSearchKey";
    public const string DisplayNameSearchKeyField = "displayNameSearchKey";
    public const string TypeNameLookupKeyField = "typeNameLookupKey";
    public const string StoreNameLookupKeyField = "storeNameLookupKey";
    public const string ScopeLookupKeyField = "scopeLookupKey";
    public const string StatusField = "status";
    public const string HasNonExpiringActiveVersionField = "hasNonExpiringActiveVersion";
    public const string MaxActiveVersionExpiresAtField = "maxActiveVersionExpiresAt";
    public const string PayloadField = "payload";
    public const string FilteredListIndex = "elsa_secrets_filtered_list";

    public static StorageUnit CreateUnit() =>
        StorageUnit.Declare(UnitId, UnitName)
            .String(TenantIdField, 256, column => column.Required())
            .String(NormalizedNameField, SecretNameConstraints.MaximumLength, column => column.Required())
            .String(NameSearchKeyField, column => column.Required())
            .String(DisplayNameSearchKeyField, column => column.Required())
            .String(TypeNameLookupKeyField, 64, column => column.Required())
            .String(StoreNameLookupKeyField, 64, column => column.Required())
            .String(ScopeLookupKeyField, 64)
            .String(StatusField, 32, column => column.Required())
            .Boolean(HasNonExpiringActiveVersionField, column => column.Required())
            .Timestamp(MaxActiveVersionExpiresAtField)
            .Json(PayloadField, column => column.Required())
            .Key(TenantIdField, NormalizedNameField)
            .Index(FilteredListIndex, TenantIdField, StatusField, NormalizedNameField)
            .OptimisticConcurrency()
            .Scoped()
            .Build();
}
