using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsPermissionsTests
{
    private const string ExpectedPermission = "Diagnostics:StructuredLogs";
    private const string ExpectedOwner = "Elsa.Diagnostics.StructuredLogs";

    [Fact]
    public void Structured_logs_contributor_has_one_stable_module_owned_permission_without_implication()
    {
        var contributor = CreateContributor();
        var permissions = contributor.Contribute().ToArray();

        Assert.Equal(ExpectedOwner, contributor.OwnerId);
        Assert.Single(permissions);

        var permission = Assert.Single(permissions);
        Assert.Equal(ExpectedPermission, permission.Key);
        Assert.Equal(ExpectedOwner, permission.OwnerId);
        Assert.Equal(contributor.ContributorType, permission.ContributorType);
        Assert.True(permission.Implies is null or { Count: 0 });
    }

    [Fact]
    public void Structured_logs_contributor_has_unique_provenance_and_excludes_the_administrative_wildcard()
    {
        var contributor = CreateContributor();
        var permissions = contributor.Contribute().ToArray();

        Assert.Equal(
            permissions.Length,
            permissions.Select(permission => PermissionKey.Normalize(permission.Key))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.DoesNotContain(permissions, permission =>
            PermissionKey.Normalize(permission.Key) == PermissionKey.Wildcard);
        Assert.All(permissions, permission =>
        {
            Assert.Equal(ExpectedOwner, permission.OwnerId);
            Assert.Equal(contributor.ContributorType, permission.ContributorType);
        });
    }

    [Fact]
    public async Task Every_structured_logs_endpoint_permission_reconciles_to_the_single_catalog_owner()
    {
        var contributor = CreateContributor();
        var catalog = new CompositePermissionCatalog(
            [new DefaultIdentityPermissionCatalog(), contributor]);
        var manifest = await CaptureStructuredLogsManifest();

        Assert.Equal(3, manifest.Length);
        foreach (var entry in manifest)
        {
            var policy = new PermissionPolicyCodec().Parse(entry.SecurityDisposition!.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
            Assert.Equal(PermissionRequirementMode.Any, policy.Descriptor!.Mode);

            var nonWildcard = policy.Descriptor.Permissions
                .Where(permission => permission != PermissionKey.Wildcard)
                .ToArray();
            Assert.Equal([PermissionKey.Normalize(ExpectedPermission)], nonWildcard);

            var definition = catalog.Find(ExpectedPermission);
            Assert.NotNull(definition);
            Assert.Equal(ExpectedOwner, definition.OwnerId);
            Assert.Equal(contributor.ContributorType, definition.ContributorType);
        }
    }

    private static IPermissionContributor CreateContributor()
    {
        var type = typeof(StructuredLogsFeature).Assembly.GetType(
            "Elsa.Diagnostics.StructuredLogs.Authorization.StructuredLogsPermissionContributor");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IPermissionContributor>(Activator.CreateInstance(type!));
    }

    private static async Task<EndpointManifestEntry[]> CaptureStructuredLogsManifest()
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        return EndpointManifestBuilder.Capture(
                host.EndpointDataSources,
                new EndpointManifestBuilderOptions(ValidateMetadata: false))
            .Entries
            .Where(entry => entry.Route.Value.Contains("structured-logs", StringComparison.Ordinal))
            .ToArray();
    }
}
