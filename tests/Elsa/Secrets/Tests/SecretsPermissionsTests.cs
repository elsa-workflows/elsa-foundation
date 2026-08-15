using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Core.Permissions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsPermissionsTests
{
    [Fact]
    public void Permission_Names_Are_Stable()
    {
        Assert.Equal("secrets:read", SecretsPermissions.Read);
        Assert.Equal("secrets:write", SecretsPermissions.Write);
        Assert.Equal("secrets:update-value", SecretsPermissions.UpdateValue);
        Assert.Equal("secrets:delete", SecretsPermissions.Delete);
        Assert.Equal("secrets:test", SecretsPermissions.Test);
        Assert.Equal("secrets:use", SecretsPermissions.Use);
        Assert.Equal("secrets:import", SecretsPermissions.Import);
        Assert.Equal("secrets:export", SecretsPermissions.Export);
    }

    [Fact]
    public void Contributor_owns_all_stable_permissions_and_only_write_implies_read()
    {
        var contributor = CreateContributor();
        var permissions = contributor.Contribute().ToArray();

        Assert.Equal("Elsa.Secrets.Api", contributor.OwnerId);
        Assert.Equal(
            [
                SecretsPermissions.Read,
                SecretsPermissions.Write,
                SecretsPermissions.UpdateValue,
                SecretsPermissions.Delete,
                SecretsPermissions.Test,
                SecretsPermissions.Use,
                SecretsPermissions.Import,
                SecretsPermissions.Export
            ],
            permissions.Select(permission => permission.Key).ToArray());
        Assert.All(permissions, permission =>
        {
            Assert.Equal(contributor.OwnerId, permission.OwnerId);
            Assert.Equal(contributor.GetType().FullName, permission.ContributorType);
            Assert.NotEmpty(permission.DisplayName);
            Assert.NotEmpty(permission.Category);
            Assert.NotEmpty(permission.Description);
        });

        Assert.Equal([SecretsPermissions.Read], permissions.Single(permission => permission.Key == SecretsPermissions.Write).Implies);
        Assert.All(
            permissions.Where(permission => permission.Key != SecretsPermissions.Write),
            permission => Assert.True(permission.Implies is null || permission.Implies.Count == 0));
        Assert.DoesNotContain(permissions, permission => permission.Key == PermissionKey.Wildcard);
    }

    [Fact]
    public void Contributor_does_not_duplicate_keys_or_publish_the_administrative_wildcard()
    {
        var permissions = CreateContributor().Contribute().ToArray();

        Assert.Equal(permissions.Length, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(permissions, permission => permission.Key == PermissionKey.Wildcard);
    }

    [Fact]
    public void Contributor_provenance_is_stable_and_module_owned()
    {
        var contributor = CreateContributor();

        Assert.Equal("Elsa.Secrets.Api", contributor.OwnerId);
        Assert.Equal(contributor.GetType().FullName, contributor.ContributorType);
    }

    [Fact]
    public void Every_http_action_permission_has_one_catalog_owner()
    {
        var catalog = CreateContributor().Contribute()
            .ToDictionary(permission => permission.Key, StringComparer.Ordinal);
        var endpointPermissions = new[]
        {
            SecretsPermissions.Read,
            SecretsPermissions.Write,
            SecretsPermissions.UpdateValue,
            SecretsPermissions.Delete,
            SecretsPermissions.Test
        };

        Assert.All(endpointPermissions, permission =>
        {
            Assert.True(catalog.TryGetValue(permission, out var definition));
            Assert.Equal("Elsa.Secrets.Api", definition!.OwnerId);
        });
    }

    private static IPermissionContributor CreateContributor()
    {
        var type = typeof(SecretsApiFeature).Assembly.GetType(
            "Elsa.Secrets.Api.Authorization.SecretsPermissionContributor");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IPermissionContributor>(Activator.CreateInstance(type!));
    }
}
