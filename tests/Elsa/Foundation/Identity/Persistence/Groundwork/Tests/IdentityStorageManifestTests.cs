using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using System.Text;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class IdentityStorageManifestTests
{
    [Fact]
    public void Manifest_declares_the_exact_identity_authority_units()
    {
        var units = IdentityV2StorageManifest.CreateUnits();
        Assert.Equal(17, units.Count);
        Assert.Equal(17, units.Select(unit => unit.Id.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_unit_has_an_ordinary_string_key_and_canonical_json_payload()
    {
        Assert.All(IdentityV2StorageManifest.CreateUnits(), unit =>
        {
            Assert.Equal(IdentityV2StorageManifest.IdField, Assert.Single(unit.Key.Columns));
            Assert.Equal(PortableType.String, Column(unit, IdentityV2StorageManifest.IdField).Type);
            Assert.Equal(PortableType.String, Column(unit, IdentityV2StorageManifest.SchemaVersionField).Type);
            Assert.Equal(PortableType.Json, Column(unit, IdentityV2StorageManifest.ContentField).Type);
        });
    }

    [Fact]
    public void Every_identity_unit_admits_optimistic_concurrency() =>
        Assert.All(IdentityV2StorageManifest.CreateUnits(), unit => Assert.True(unit.Concurrency.IsOptimistic));

    [Fact]
    public void Only_the_global_provider_configuration_is_global()
    {
        var units = IdentityV2StorageManifest.CreateUnits();
        Assert.Equal(16, units.Count(unit => unit.Scope == ScopePolicy.Scoped));
        Assert.Equal(IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            Assert.Single(units, unit => unit.Scope == ScopePolicy.Global).Id.Value);
    }

    [Fact]
    public void Physical_storage_names_are_declared_and_collision_free()
    {
        var names = IdentityV2StorageManifest.CreateUnits().Select(unit => unit.Name).ToArray();
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void User_authority_has_both_nullable_normalized_lookup_indexes()
    {
        var unit = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityUserDocumentKind);
        AssertNullableIndexed(unit, IdentityStorageManifest.NormalizedUserNameKeyField);
        AssertNullableIndexed(unit, IdentityStorageManifest.NormalizedEmailKeyField);
    }

    [Fact]
    public void Role_authority_has_tenant_and_normalized_name_indexes()
    {
        var unit = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityRoleDocumentKind);
        AssertIndexed(unit, IdentityStorageManifest.NormalizedRoleNameKeyField);
        AssertIndexed(unit, IdentityStorageManifest.TenantIdField);
        Assert.False(Column(unit, IdentityStorageManifest.TenantIdField).IsNullable);
    }

    [Fact]
    public void Relationship_units_declare_their_bounded_lookup_indexes()
    {
        AssertIndexed(IdentityV2StorageManifest.Require(IdentityStorageManifest.UserClaimDocumentKind), IdentityStorageManifest.UserLookupKeyField);
        AssertIndexed(IdentityV2StorageManifest.Require(IdentityStorageManifest.UserClaimDocumentKind), IdentityStorageManifest.ClaimKeyField);
        AssertIndexed(IdentityV2StorageManifest.Require(IdentityStorageManifest.UserRoleDocumentKind), IdentityStorageManifest.UserLookupKeyField);
        AssertIndexed(IdentityV2StorageManifest.Require(IdentityStorageManifest.UserRoleDocumentKind), IdentityStorageManifest.RoleLookupKeyField);
        AssertIndexed(IdentityV2StorageManifest.Require(IdentityStorageManifest.ExternalLoginDocumentKind), IdentityStorageManifest.UserLookupKeyField);
    }

    [Fact]
    public void Mutation_receipts_have_a_portable_oldest_expired_bounded_route()
    {
        var unit = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityMutationReceiptDocumentKind);
        Assert.Equal(PortableType.DateTimeOffset, Column(unit, IdentityStorageManifest.MutationReceiptExpiresAtField).Type);
        AssertIndexed(unit, IdentityStorageManifest.MutationReceiptExpiresAtField);
    }

    [Theory]
    [InlineData(IdentityStorageManifest.FindUserByNormalizedNameQuery, IdentityStorageManifest.IdentityUserDocumentKind)]
    [InlineData(IdentityStorageManifest.FindUserByNormalizedEmailQuery, IdentityStorageManifest.IdentityUserDocumentKind)]
    [InlineData(IdentityStorageManifest.FindRoleByNormalizedNameQuery, IdentityStorageManifest.IdentityRoleDocumentKind)]
    [InlineData(IdentityStorageManifest.ListRolesByTenantQuery, IdentityStorageManifest.IdentityRoleDocumentKind)]
    [InlineData(IdentityStorageManifest.ListUserClaimsQuery, IdentityStorageManifest.UserClaimDocumentKind)]
    [InlineData(IdentityStorageManifest.FindUsersByClaimQuery, IdentityStorageManifest.UserClaimDocumentKind)]
    [InlineData(IdentityStorageManifest.ListRoleClaimsQuery, IdentityStorageManifest.RoleClaimDocumentKind)]
    [InlineData(IdentityStorageManifest.ListUserRolesQuery, IdentityStorageManifest.UserRoleDocumentKind)]
    [InlineData(IdentityStorageManifest.ListRoleUsersQuery, IdentityStorageManifest.UserRoleDocumentKind)]
    [InlineData(IdentityStorageManifest.ListUserLoginsQuery, IdentityStorageManifest.ExternalLoginDocumentKind)]
    [InlineData(IdentityStorageManifest.ListClaimMappingsByProviderQuery, IdentityStorageManifest.IdentityClaimMappingDocumentKind)]
    [InlineData(IdentityStorageManifest.ListExpiredMutationReceiptsQuery, IdentityStorageManifest.IdentityMutationReceiptDocumentKind)]
    public void Every_scale_bearing_query_names_an_index_from_its_declared_unit(
        string queryIdentity,
        string unitId)
    {
        var expectedIndex = IdentityV2StorageManifest.IndexForQuery(queryIdentity);
        var unit = IdentityV2StorageManifest.Require(unitId);

        Assert.Contains(unit.Indexes, index => index.Name == expectedIndex);
    }

    [Fact]
    public void Projected_string_index_columns_fit_sql_server_key_budget()
    {
        var columns = IdentityV2StorageManifest.CreateUnits()
            .SelectMany(unit => unit.Indexes.SelectMany(index => index.Columns.Select(column => Column(unit, column.Column))))
            .Where(column => column.Type == PortableType.String);
        Assert.All(columns, column => Assert.True(
            column.MaxLength * IdentityStorageManifest.SqlServerUnicodeBytesPerCodeUnit <=
            IdentityStorageManifest.SqlServerMaxNonclusteredIndexKeyBytes));
    }

    [Theory]
    [InlineData("ascii-i", "i", "I", true)]
    [InlineData("turkish-dotted-i", "i", "İ", false)]
    [InlineData("turkish-dotless-i", "I", "ı", false)]
    [InlineData("sharp-s", "ß", "ẞ", true)]
    [InlineData("sharp-s-expansion", "ß", "ss", false)]
    [InlineData("greek-final-sigma", "Σ", "ς", true)]
    [InlineData("kelvin-sign", "K", "k", true)]
    [InlineData("ligature-expansion", "ﬀ", "FF", false)]
    [InlineData("precomposed-accent", "é", "É", true)]
    [InlineData("decomposed-accent", "é", "e\u0301", false)]
    [InlineData("garay-supplementary", "\U00010D70", "\U00010D50", true)]
    [InlineData("deseret-supplementary", "\U00010428", "\U00010400", true)]
    public void Lookup_keys_use_provider_independent_unicode_case_evidence(string caseId, string left, string right, bool expectedEqual)
    {
        Assert.Equal(expectedEqual, IdentityDocumentId.From("tenant-alpha", left) == IdentityDocumentId.From("tenant-alpha", right));
        Assert.NotEmpty(caseId);
    }

    [Fact]
    public void Lookup_keys_apply_the_complete_unicode16_garay_case_mapping_on_every_runtime()
    {
        for (var capital = 0x10D50; capital <= 0x10D65; capital++)
            Assert.Equal(IdentityDocumentId.From("tenant-alpha", new Rune(capital + 0x20).ToString()),
                IdentityDocumentId.From("tenant-alpha", new Rune(capital).ToString()));
    }

    [Fact]
    public void Composite_lookup_normalization_preserves_unpaired_surrogates_around_garay_mapping() =>
        Assert.Equal(string.Concat("x", "\uD800", "\U00010D70", "y"),
            IdentityCompositeDocumentId.Normalize(string.Concat("x", "\uD800", "\U00010D50", "y")));

    [Fact]
    public void Document_ids_frame_key_parts_without_separator_collisions() =>
        Assert.NotEqual(IdentityDocumentId.From("alpha\u001fbeta", "gamma"), IdentityDocumentId.From("alpha", "beta\u001fgamma"));

    [Fact]
    public void Request_fingerprints_frame_parts_without_separator_collisions() =>
        Assert.NotEqual(IdentityRequestFingerprint.FromParts("alpha\u001ebeta", "gamma"), IdentityRequestFingerprint.FromParts("alpha", "beta\u001egamma"));

    private static ColumnDefinition Column(StorageUnit unit, string name) => unit.Columns.Single(column => column.Name == name);

    private static void AssertNullableIndexed(StorageUnit unit, string column)
    {
        Assert.True(Column(unit, column).IsNullable);
        AssertIndexed(unit, column);
    }

    private static void AssertIndexed(StorageUnit unit, string column) =>
        Assert.Contains(unit.Indexes, index => index.Columns.Any(indexColumn => indexColumn.Column == column));
}
