using Elsa.Foundation.Identity.OpenIddict.Groundwork;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkStorageManifestTests
{
    [Theory]
    [InlineData("Application")]
    [InlineData("Authorization")]
    [InlineData("Scope")]
    [InlineData("Token")]
    public void Every_logical_record_unit_has_a_declared_storage_definition(string recordKind)
    {
        var definition = recordKind switch
        {
            "Application" => OpenIddictGroundworkStorageManifest.CreateApplicationDefinition(),
            "Authorization" => OpenIddictGroundworkStorageManifest.CreateAuthorizationDefinition(),
            "Scope" => OpenIddictGroundworkStorageManifest.CreateScopeDefinition(),
            "Token" => OpenIddictGroundworkStorageManifest.CreateTokenDefinition(),
            _ => throw new ArgumentOutOfRangeException(nameof(recordKind))
        };

        Assert.NotEmpty(definition.ProjectedColumns);
    }

    [Fact]
    public void Manifest_contract_exposes_bounded_routes_and_schema_fingerprint()
    {
        Assert.Contains(OpenIddictGroundworkStorageManifest.BoundedRoutes, route =>
            route.RouteIdentity == OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery);
        Assert.Matches("^[A-Fa-f0-9]{64}$", OpenIddictGroundworkStorageManifest.Fingerprint);
    }

    [Fact]
    public void Authorization_and_token_projections_use_canonical_record_paths()
    {
        var authorizationPaths = OpenIddictGroundworkStorageManifest
            .CreateAuthorizationDefinition()
            .ProjectedColumns
            .Select(column => column.Path)
            .ToHashSet(StringComparer.Ordinal);
        var tokenPaths = OpenIddictGroundworkStorageManifest
            .CreateTokenDefinition()
            .ProjectedColumns
            .Select(column => column.Path)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["applicationId", "creationDate", "scopes", "status", "subject", "type"],
            authorizationPaths.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "applicationId",
                "authorizationId",
                "creationDate",
                "expirationDate",
                "referenceId",
                "status",
                "subject",
                "type"
            ],
            tokenPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Preview_102_routes_subject_lookups_to_provider_bounded_cursor_indexes()
    {
        var manifest = OpenIddictGroundworkStorageManifest.Create();
        var envelope = new DocumentEnvelopeDefinition();
        AssertSubjectIndexEvolution(
            manifest,
            OpenIddictGroundworkJson.AuthorizationDocumentKind,
            OpenIddictGroundworkStorageManifest.FindAuthorizationBySubjectQuery,
            "openiddict-authorization-by-subject-application-status-type",
            ["subject", "applicationId", "status", "type"],
            ["subject"],
            envelope.IdLookupKeyColumn,
            expectLegacyIndex: false);
        AssertSubjectIndexEvolution(
            manifest,
            OpenIddictGroundworkJson.TokenDocumentKind,
            OpenIddictGroundworkStorageManifest.FindTokenBySubjectQuery,
            "openiddict-token-by-subject-status-expiration",
            ["subject", "status", "expirationDate"],
            ["subject"],
            envelope.IdLookupKeyColumn,
            expectLegacyIndex: true);
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            ProviderPhysicalNameNormalizer.Identity);

        Assert.True(
            resolution.IsValid,
            string.Join(Environment.NewLine, resolution.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    private static void AssertSubjectIndexEvolution(
        StorageManifest manifest,
        string documentKind,
        string queryIdentity,
        string legacyIndexIdentity,
        IReadOnlyList<string> legacyColumns,
        IReadOnlyList<string> currentColumns,
        string identityTieBreakColumn,
        bool expectLegacyIndex)
    {
        var unit = manifest.StorageUnits.Single(unit => unit.Identity.Value == documentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var definition = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var currentIndexIdentity = $"{legacyIndexIdentity}-v2";
        var query = storage.BoundedQueries.Single(query => query.Identity == queryIdentity);

        Assert.Equal(currentIndexIdentity, query.IndexIdentity);
        Assert.Equal(QueryPagingSupport.Cursor, query.PagingSupport);
        Assert.Equal(BoundedQueryPredicateBindingMode.DeclaredFields, query.PredicateBindingMode);
        var predicate = Assert.Single(query.PredicateFields);
        Assert.Equal("subject", predicate.Path);
        Assert.Equal([PortableQueryOperation.Equal], predicate.Operations);

        if (expectLegacyIndex)
        {
            Assert.Equal(
                legacyColumns,
                storage.LogicalIndexes.Single(index => index.Identity == legacyIndexIdentity)
                    .Fields.Select(field => field.Path));
        }
        else
        {
            Assert.DoesNotContain(storage.LogicalIndexes, index => index.Identity == legacyIndexIdentity);
        }
        Assert.Equal(
            currentColumns,
            storage.LogicalIndexes.Single(index => index.Identity == currentIndexIdentity)
                .Fields.Select(field => field.Path));
        if (expectLegacyIndex)
        {
            Assert.Equal(
                legacyColumns,
                definition.Indexes.Single(index => index.LogicalName == legacyIndexIdentity)
                    .Columns.Select(column => column.ColumnLogicalName));
        }
        else
        {
            Assert.DoesNotContain(definition.Indexes, index => index.LogicalName == legacyIndexIdentity);
        }
        Assert.Equal(
            currentColumns.Append(identityTieBreakColumn),
            definition.Indexes.Single(index => index.LogicalName == currentIndexIdentity)
                .Columns.Select(column => column.ColumnLogicalName));
    }
}
