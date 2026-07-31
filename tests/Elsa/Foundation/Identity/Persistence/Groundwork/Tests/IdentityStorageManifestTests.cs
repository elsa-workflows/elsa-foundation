using System.Text;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class IdentityStorageManifestTests
{
    private static readonly DocumentEnvelopeDefinition Envelope = new();

    private static readonly string[] ExpectedAuthorityUnits =
    [
        "identityUser",
        "identityRole",
        "identityApplication",
        "identityCredential",
        "identityClaimMapping",
        "identityProviderConfiguration",
        "identityGlobalProviderConfiguration",
        "identityUserClaim",
        "identityRoleClaim",
        "identityExternalLogin",
        "identityUserRole",
        "identityUserToken",
        "identityTenantMembership",
        "identityUserNameReservation",
        "identityEmailReservation",
        "identityRoleNameReservation",
        "identityMutationReceipt"
    ];

    private static readonly string[] ExpectedBoundedQueries =
    [
        "find-user-by-normalized-name",
        "find-user-by-normalized-email",
        "find-role-by-normalized-name",
        "list-roles-by-tenant",
        "list-user-claims",
        "find-users-by-claim",
        "list-role-claims",
        "list-user-roles",
        "list-role-users",
        "list-user-logins",
        "list-claim-mappings-by-provider",
        "list-expired-mutation-receipts"
    ];

    [Fact]
    public void Manifest_declares_the_exact_identity_authority_units()
    {
        Assert.Equal("1.0.6", IdentityStorageManifest.SchemaVersion);
        var actual = IdentityStorageManifest.Create()
            .StorageUnits
            .Select(unit => unit.Identity.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedAuthorityUnits.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Manifest_resolves_every_scale_bearing_route_with_exact_physical_order_evidence()
    {
        var manifest = IdentityStorageManifest.Create();
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            ProviderPhysicalNameNormalizer.Identity);

        Assert.True(
            resolution.IsValid,
            string.Join(Environment.NewLine, resolution.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Equal(manifest.StorageUnits.Count, resolution.Definitions.Count);
    }

    [Fact]
    public void Every_query_is_bound_to_an_optimized_index_on_the_same_unit()
    {
        var manifest = IdentityStorageManifest.Create();
        var queryNames = manifest.StorageUnits
            .SelectMany(unit => unit.PhysicalStorage!.BoundedQueries.Select(query => query.Identity))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedBoundedQueries.Order(StringComparer.Ordinal), queryNames);

        foreach (var unit in manifest.StorageUnits)
        {
            var physicalStorage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = ExplicitTableDefinition(physicalStorage.Policy);
            foreach (var query in physicalStorage.BoundedQueries)
            {
                var logicalIndex = Assert.Single(
                    physicalStorage.LogicalIndexes,
                    candidate => candidate.Identity == query.IndexIdentity);
                var physicalIndex = Assert.Single(
                    table.Indexes,
                    candidate => candidate.LogicalName == query.IndexIdentity);
                Assert.Equal(QueryPagingSupport.Cursor, query.PagingSupport);
                Assert.False(logicalIndex.IsUnique);
                Assert.False(physicalIndex.IsUnique);
                Assert.Equal(Envelope.StorageScopeColumn, physicalIndex.Columns[0].ColumnLogicalName);
                Assert.Equal(
                    logicalIndex.Fields.Select(field => field.Path),
                    physicalIndex.Columns.Skip(1).SkipLast(1).Select(column => column.ColumnLogicalName));
                Assert.Equal(Envelope.IdLookupKeyColumn, physicalIndex.Columns[^1].ColumnLogicalName);
            }
        }
    }

    [Fact]
    public async Task Manifest_source_declares_required_routes_and_capabilities()
    {
        var declaration = await new IdentityGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);

        Assert.Contains("schema-history", declaration.Manifest.RequiredCapabilities);
        Assert.Contains("optimistic-concurrency", declaration.Manifest.RequiredCapabilities);
        Assert.Contains(
            declaration.TopologyRequirements,
            requirement => requirement.Identity == RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity);
        var atomicAuthorityKinds = new[]
        {
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityStorageManifest.UserClaimDocumentKind,
            IdentityStorageManifest.RoleClaimDocumentKind,
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityStorageManifest.UserTokenDocumentKind,
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityStorageManifest.RoleNameReservationDocumentKind,
            IdentityStorageManifest.IdentityMutationReceiptDocumentKind
        };
        Assert.All(atomicAuthorityKinds, kind => Assert.Contains(
            declaration.RequiredRoutes,
            route => route.StorageUnit.Value == kind &&
                     route.RequiredCapabilities.Contains(WellKnownCapabilities.AtomicCommit)));
    }

    [Fact]
    public async Task Physical_storage_names_are_declared_and_collision_free()
    {
        var declaration = await new IdentityGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);
        var unitIdentities = declaration.Manifest.StorageUnits
            .Select(unit => unit.Identity.Value)
            .ToArray();

        Assert.All(declaration.Manifest.StorageUnits, unit => Assert.NotNull(unit.PhysicalStorage));
        Assert.Equal(unitIdentities.Length, unitIdentities.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Primary_id_only_units_use_dedicated_document_tables_and_indexed_units_use_entity_tables()
    {
        var units = IdentityStorageManifest.Create().StorageUnits.ToDictionary(unit => unit.Identity.Value);
        var dedicated = new HashSet<string>(StringComparer.Ordinal)
        {
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityStorageManifest.UserTokenDocumentKind,
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityStorageManifest.UserNameReservationDocumentKind,
            IdentityStorageManifest.EmailReservationDocumentKind,
            IdentityStorageManifest.RoleNameReservationDocumentKind
        };

        Assert.All(units, pair => Assert.Equal(
            dedicated.Contains(pair.Key)
                ? PhysicalStorageForm.DedicatedDocumentTable
                : PhysicalStorageForm.PhysicalEntityTable,
            ExplicitTableDefinition(pair.Value.PhysicalStorage!.Policy).Form));
    }

    [Fact]
    public void Only_optional_normalized_lookup_projections_are_nullable_and_exclude_missing_values()
    {
        var expectedNullable = new HashSet<(string DocumentKind, string Field)>
        {
            (IdentityStorageManifest.IdentityUserDocumentKind, IdentityStorageManifest.NormalizedUserNameKeyField),
            (IdentityStorageManifest.IdentityUserDocumentKind, IdentityStorageManifest.NormalizedEmailKeyField),
            (IdentityStorageManifest.IdentityRoleDocumentKind, IdentityStorageManifest.NormalizedRoleNameKeyField)
        };
        var manifest = IdentityStorageManifest.Create();

        foreach (var unit in manifest.StorageUnits)
        {
            var physical = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = ExplicitTableDefinition(physical.Policy);
            Assert.All(table.ProjectedColumns, column =>
                Assert.Equal(
                    expectedNullable.Contains((unit.Identity.Value, column.LogicalName)),
                    column.IsNullable));
            Assert.All(physical.LogicalIndexes, index =>
                Assert.Equal(MissingValueBehavior.Excluded, index.MissingValueBehavior));
        }

        var actualNullable = manifest.StorageUnits
            .SelectMany(unit => ExplicitTableDefinition(unit.PhysicalStorage!.Policy).ProjectedColumns
                .Where(column => column.IsNullable)
                .Select(column => (unit.Identity.Value, column.LogicalName)))
            .ToHashSet();
        Assert.Equal(expectedNullable, actualNullable);
    }

    [Fact]
    public void Mutation_receipts_have_a_portable_oldest_expired_bounded_route()
    {
        var unit = IdentityStorageManifest.Create().StorageUnits.Single(candidate =>
            candidate.Identity.Value == IdentityStorageManifest.IdentityMutationReceiptDocumentKind);
        var query = Assert.Single(unit.PhysicalStorage.BoundedQueries);
        var index = Assert.Single(unit.PhysicalStorage.LogicalIndexes, candidate =>
            candidate.Identity == query.IndexIdentity);
        Assert.Contains(unit.PhysicalStorage.LogicalIndexes, candidate =>
            candidate.Identity == "identity-mutation-receipt-by-expiry");
        Assert.Equal("identity-mutation-receipt-by-expiry-v2", query.IndexIdentity);
        var table = ExplicitTableDefinition(unit.PhysicalStorage.Policy);
        var expiry = Assert.Single(table.ProjectedColumns);

        Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, expiry.Path);
        Assert.Equal(PortablePhysicalType.DateTime, expiry.Type);
        Assert.Equal(IndexValueKind.DateTime, index.ValueKind);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, query.Operations);
        Assert.Equal(QuerySortSupport.Ascending, query.SortSupport);
        Assert.Equal(QueryPagingSupport.Cursor, query.PagingSupport);
        Assert.Equal(
            new BoundedQuerySortField(
                IdentityStorageManifest.MutationReceiptExpiresAtField,
                PhysicalSortDirection.Ascending),
            Assert.Single(query.SortFields));
        var predicate = Assert.Single(query.PredicateFields);
        Assert.Equal(IdentityStorageManifest.MutationReceiptExpiresAtField, predicate.Path);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, predicate.Operations);
    }

    [Fact]
    public async Task Projected_string_index_columns_fit_sql_server_key_budget()
    {
        var declaration = await new IdentityGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);
        var tables = declaration.Manifest.StorageUnits
            .Where(unit => unit.PhysicalStorage is not null)
            .Select(unit => ExplicitTableDefinition(unit.PhysicalStorage!.Policy))
            .ToArray();

        Assert.NotEmpty(tables);
        foreach (var table in tables)
        {
            Assert.All(table.ProjectedColumns.Where(column => column.Type == PortablePhysicalType.String), column =>
                Assert.True(
                    column.Length is > 0 and <= IdentityStorageManifest.ProjectedLookupColumnLength,
                    $"Projected string column '{column.LogicalName}' must declare a SQL Server-safe bounded length."));

            foreach (var index in table.Indexes)
            {
                var declaredKeyBytes = index.Columns.Sum(column =>
                {
                    if (column.ColumnLogicalName == Envelope.StorageScopeColumn)
                        return IdentityStorageManifest.SqlServerStorageScopeKeyBytes;
                    if (column.ColumnLogicalName == Envelope.IdLookupKeyColumn)
                        return IdentityStorageManifest.SqlServerDocumentIdentityLookupKeyBytes;
                    var projected = table.ProjectedColumns.Single(candidate =>
                        candidate.LogicalName == column.ColumnLogicalName);
                    return projected.Type switch
                    {
                        PortablePhysicalType.String =>
                            (projected.Length ?? 0) * IdentityStorageManifest.SqlServerUnicodeBytesPerCodeUnit,
                        PortablePhysicalType.DateTime => IdentityStorageManifest.SqlServerDateTime2KeyBytes,
                        _ => throw new InvalidOperationException(
                            $"SQL Server key-width evidence does not cover projected type '{projected.Type}'.")
                    };
                });
                Assert.True(
                    declaredKeyBytes <= IdentityStorageManifest.SqlServerMaxNonclusteredIndexKeyBytes,
                    $"Physical index '{index.LogicalName}' declares {declaredKeyBytes} SQL Server key bytes.");
            }
        }
    }

    [Fact]
    public async Task Physical_identifiers_and_route_parameters_fit_required_provider_limits()
    {
        var declaration = await new IdentityGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);

        foreach (var unit in declaration.Manifest.StorageUnits.Where(unit => unit.PhysicalStorage is not null))
        {
            var table = ExplicitTableDefinition(unit.PhysicalStorage!.Policy);
            Assert.True(table.FeatureDefaultLogicalName is { Length: <= 128 }, table.FeatureDefaultLogicalName);
            Assert.All(table.ProjectedColumns, column => Assert.True(column.LogicalName.Length <= 128, column.LogicalName));
            Assert.All(table.Indexes, index => Assert.True(index.LogicalName.Length <= 128, index.LogicalName));

            foreach (var query in unit.PhysicalStorage.BoundedQueries)
            {
                var providerParameters = query.PredicateFields.Count;
                Assert.True(
                    providerParameters <= IdentityStorageManifest.MaxBoundedQueryParameters,
                    $"Bounded query '{query.Identity}' requires {providerParameters} provider parameters.");
            }
        }
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
    public void Lookup_keys_use_provider_independent_unicode_case_evidence(
        string caseId,
        string left,
        string right,
        bool expectedEqual)
    {
        var leftKey = IdentityDocumentId.From("tenant-alpha", left);
        var rightKey = IdentityDocumentId.From("tenant-alpha", right);

        Assert.Equal(expectedEqual, string.Equals(leftKey, rightKey, StringComparison.Ordinal));
        Assert.NotEmpty(caseId);
    }

    [Fact]
    public void Lookup_keys_apply_the_complete_unicode16_garay_case_mapping_on_every_runtime()
    {
        for (var capital = 0x10D50; capital <= 0x10D65; capital++)
        {
            var uppercase = new Rune(capital).ToString();
            var lowercase = new Rune(capital + 0x20).ToString();

            Assert.Equal(
                IdentityDocumentId.From("tenant-alpha", lowercase),
                IdentityDocumentId.From("tenant-alpha", uppercase));
            Assert.Equal(
                IdentityCompositeDocumentId.Normalize(lowercase),
                IdentityCompositeDocumentId.Normalize(uppercase));
        }
    }

    [Fact]
    public void Composite_lookup_normalization_preserves_unpaired_surrogates_around_garay_mapping()
    {
        var input = string.Concat("x", "\uD800", "\U00010D50", "y");
        var expected = string.Concat("x", "\uD800", "\U00010D70", "y");

        Assert.Equal(expected, IdentityCompositeDocumentId.Normalize(input));
    }

    [Fact]
    public void Document_ids_frame_key_parts_without_separator_collisions()
    {
        var left = IdentityDocumentId.From("alpha\u001fbeta", "gamma");
        var right = IdentityDocumentId.From("alpha", "beta\u001fgamma");

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Request_fingerprints_frame_parts_without_separator_collisions()
    {
        var left = IdentityRequestFingerprint.FromParts("alpha\u001ebeta", "gamma");
        var right = IdentityRequestFingerprint.FromParts("alpha", "beta\u001egamma");

        Assert.NotEqual(left, right);
    }

    private static PhysicalTableDefinition ExplicitTableDefinition(object policy)
    {
        var definition = policy.GetType().GetProperty("Definition")?.GetValue(policy);
        return Assert.IsType<PhysicalTableDefinition>(definition);
    }
}
