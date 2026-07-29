using Elsa.Foundation.Identity.OpenIddict.Groundwork;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Core.Indexing;
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

    [Theory]
    [InlineData(OpenIddictGroundworkJson.ApplicationDocumentKind, OpenIddictGroundworkStorageManifest.ListApplicationsQuery)]
    [InlineData(OpenIddictGroundworkJson.AuthorizationDocumentKind, OpenIddictGroundworkStorageManifest.ListAuthorizationsQuery)]
    [InlineData(OpenIddictGroundworkJson.ScopeDocumentKind, OpenIddictGroundworkStorageManifest.ListScopesQuery)]
    [InlineData(OpenIddictGroundworkJson.TokenDocumentKind, OpenIddictGroundworkStorageManifest.ListTokensQuery)]
    public void Count_and_list_routes_are_explicitly_unfiltered_and_identity_ordered(
        string documentKind,
        string queryIdentity)
    {
        var storage = OpenIddictGroundworkStorageManifest.Create().StorageUnits
            .Single(unit => unit.Identity.Value == documentKind)
            .PhysicalStorage!;
        var query = storage.BoundedQueries.Single(candidate => candidate.Identity == queryIdentity);

        Assert.Equal(BoundedQueryPredicateBindingMode.DeclaredFields, query.PredicateBindingMode);
        Assert.Empty(query.PredicateFields);
        Assert.True(query.SupportsTotalCount);
        Assert.Equal(["id"], query.SortFields.Select(field => field.Path));
        Assert.Equal(
            new[] { BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count },
            query.ResultOperations.Order());
    }

    [Fact]
    public void Token_storage_projects_every_bounded_route_field_with_portable_types()
    {
        var definition = OpenIddictGroundworkStorageManifest.CreateTokenDefinition();
        var columns = definition.ProjectedColumns.ToDictionary(column => column.Path, StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "applicationId",
                "authorizationId",
                "creationDate",
                "expirationDate",
                "referenceId",
                "status",
                "subject",
                "type"
            },
            columns.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PortablePhysicalType.DateTime, columns["creationDate"].Type);
        Assert.Equal(PortablePhysicalType.DateTime, columns["expirationDate"].Type);
        Assert.All(columns.Values.Where(column => column.Type == PortablePhysicalType.String), column =>
            Assert.InRange(column.Length!.Value, 1, 256));
        Assert.Contains(definition.Indexes, index =>
            index.Columns.Take(4).Select(column => column.ColumnLogicalName)
                .SequenceEqual(["subject", "applicationId", "status", "type"], StringComparer.Ordinal));
    }

    [Fact]
    public void Authorization_storage_projects_only_canonical_lifecycle_and_filter_paths()
    {
        var definition = OpenIddictGroundworkStorageManifest.CreateAuthorizationDefinition();
        var columns = definition.ProjectedColumns.ToDictionary(column => column.Path, StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "applicationId",
                "creationDate",
                "scopes",
                "status",
                "subject",
                "type"
            },
            columns.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PortablePhysicalType.DateTime, columns["creationDate"].Type);
        Assert.DoesNotContain("expiration", columns.Keys);
        Assert.Contains(definition.Indexes, index =>
            index.Columns.Select(column => column.ColumnLogicalName)
                .SequenceEqual(["subject", "applicationId", "status", "type"], StringComparer.Ordinal));
    }

    [Fact]
    public void Scope_set_and_resource_routes_are_bounded_without_client_sorting()
    {
        var scope = OpenIddictGroundworkStorageManifest.Create().StorageUnits.Single(unit =>
            unit.Identity.Value == OpenIddictGroundworkJson.ScopeDocumentKind);
        var storage = scope.PhysicalStorage!;
        var names = storage.BoundedQueries.Single(query =>
            query.Identity == OpenIddictGroundworkStorageManifest.FindScopesByNamesQuery);
        var resource = storage.BoundedQueries.Single(query =>
            query.Identity == OpenIddictGroundworkStorageManifest.FindScopeByResourceQuery);

        Assert.Equal(QuerySortSupport.None, names.SortSupport);
        Assert.Empty(names.SortFields);
        Assert.True(names.SupportsTotalCount);
        Assert.Equal(
            [PortableQueryOperation.In],
            Assert.Single(names.PredicateFields).Operations);
        Assert.Equal(QuerySortSupport.None, resource.SortSupport);
        Assert.Empty(resource.SortFields);
        Assert.True(resource.SupportsTotalCount);
        Assert.Equal(
            [PortableQueryOperation.CollectionContains],
            Assert.Single(resource.PredicateFields).Operations);
        Assert.All(
            new[] { names, resource },
            query => Assert.Equal(
                new[] { BoundedQueryResultOperation.Documents, BoundedQueryResultOperation.Count },
                query.ResultOperations.Order()));
    }

    [Fact]
    public void Token_unit_declares_query_routes_but_not_unimplemented_mutations()
    {
        var token = OpenIddictGroundworkStorageManifest.Create().StorageUnits.Single(unit =>
            unit.Identity.Value == OpenIddictGroundworkJson.TokenDocumentKind);
        var storage = token.PhysicalStorage!;

        Assert.Equal(
            new[]
            {
                OpenIddictGroundworkStorageManifest.FindTokenQuery,
                OpenIddictGroundworkStorageManifest.FindTokenByApplicationIdQuery,
                OpenIddictGroundworkStorageManifest.FindTokenByAuthorizationIdQuery,
                OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery,
                OpenIddictGroundworkStorageManifest.FindTokenBySubjectQuery,
                OpenIddictGroundworkStorageManifest.ListTokensQuery
            },
            storage.BoundedQueries.Select(query => query.Identity).Order(StringComparer.Ordinal));
        var list = storage.BoundedQueries.Single(query =>
            query.Identity == OpenIddictGroundworkStorageManifest.ListTokensQuery);
        Assert.Contains(BoundedQueryResultOperation.Count, list.ResultOperations);
        Assert.Equal(["id"], list.SortFields.Select(field => field.Path));

        Assert.Empty(storage.BoundedMutations);
    }
}
