using Elsa.Foundation.Identity.OpenIddict.Groundwork;

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
}
