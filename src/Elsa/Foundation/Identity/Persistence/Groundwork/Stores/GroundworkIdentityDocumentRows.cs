using System.Text.Json;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

internal static class GroundworkIdentityDocumentRows
{
    public static TDocument Deserialize<TDocument>(GroundworkIdentityRow row) =>
        JsonSerializer.Deserialize<TDocument>(row.CanonicalJson, IdentityGroundworkJson.Options)
        ?? throw new InvalidDataException(
            $"Identity row '{row.UnitId}/{row.Id}' did not contain a '{typeof(TDocument).Name}' document.");

    public static GroundworkIdentityRowWrite Write<TDocument>(
        string unitId,
        string id,
        TDocument document,
        long? expectedVersion = null,
        IReadOnlyDictionary<string, object?>? projectedValues = null) =>
        new(
            unitId,
            id,
            JsonSerializer.Serialize(document, IdentityGroundworkJson.Options),
            projectedValues ?? ProjectedValues(document),
            Condition(expectedVersion));

    public static GroundworkIdentityRowWriteCondition Condition(long? expectedVersion) => expectedVersion switch
    {
        null => GroundworkIdentityRowWriteCondition.Unconditional,
        0 => GroundworkIdentityRowWriteCondition.CreateOnly,
        > 0 => GroundworkIdentityRowWriteCondition.IfVersion(expectedVersion.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(expectedVersion), "An expected Identity row version cannot be negative.")
    };

    public static IReadOnlyDictionary<string, object?> ProjectedValues<TDocument>(TDocument document) => document switch
    {
        IdentityUserDocument value => Values(
            (IdentityStorageManifest.NormalizedUserNameKeyField, value.NormalizedUserNameKey),
            (IdentityStorageManifest.NormalizedEmailKeyField, value.NormalizedEmailKey)),
        IdentityRoleDocument value => Values(
            (IdentityStorageManifest.NormalizedRoleNameKeyField, value.NormalizedRoleNameKey),
            (IdentityStorageManifest.TenantIdField, value.TenantId)),
        IdentityClaimMappingDocument value => Values((IdentityStorageManifest.ProviderLookupKeyField, value.ProviderLookupKey)),
        IdentityUserClaimDocument value => Values(
            (IdentityStorageManifest.UserLookupKeyField, value.UserLookupKey),
            (IdentityStorageManifest.ClaimKeyField, value.ClaimKey)),
        IdentityRoleClaimDocument value => Values((IdentityStorageManifest.RoleLookupKeyField, value.RoleLookupKey)),
        IdentityExternalLoginDocument value => Values((IdentityStorageManifest.UserLookupKeyField, value.UserLookupKey)),
        IdentityUserRoleDocument value => Values(
            (IdentityStorageManifest.UserLookupKeyField, value.UserLookupKey),
            (IdentityStorageManifest.RoleLookupKeyField, value.RoleLookupKey)),
        _ => EmptyProjectedValues
    };

    private static IReadOnlyDictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> EmptyProjectedValues { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
