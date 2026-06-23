using System.Security.Claims;
using Elsa.Server;
using Elsa.Server.ExtensionBuilder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ExtensionBuilderAuthorizationTests
{
    private const string ElsaIdentityRoleClaimType = "elsa.identity.role";

    [Fact]
    public void CreateCallerMarksAuthenticatedTrustedRoleAsTrusted()
    {
        var context = CreateContext(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "sipke"),
            new Claim(ClaimTypes.Name, "Sipke"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "test"));

        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.Equal("sipke", caller.OwnerId);
        Assert.True(caller.HasManagementAccess);
        Assert.True(caller.IsTrusted);
    }

    [Fact]
    public void CreateCallerMarksElsaNormalizedTrustedRoleAsTrusted()
    {
        var context = CreateContext(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "sipke"),
            new Claim(ElsaIdentityRoleClaimType, "Admin")
        ], "test"));

        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.True(caller.IsTrusted);
    }

    [Fact]
    public void CreateCallerDoesNotTrustUnauthenticatedOrUnlistedRole()
    {
        var anonymousWithoutManagementKey = ElsaExtensionBuilderApi.CreateCaller(CreateContext(new ClaimsIdentity()), hasManagementAccess: false);
        var anonymousWithManagementKey = ElsaExtensionBuilderApi.CreateCaller(CreateContext(new ClaimsIdentity()), hasManagementAccess: true);
        var developer = ElsaExtensionBuilderApi.CreateCaller(CreateContext(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "dev"),
            new Claim(ClaimTypes.Role, "Developer")
        ], "test")), hasManagementAccess: true);

        Assert.False(anonymousWithoutManagementKey.IsTrusted);
        Assert.False(anonymousWithManagementKey.IsTrusted);
        Assert.False(developer.IsTrusted);
    }

    [Fact]
    public void ManagementApiKeyPrincipalMakesServerPipelineCallerTrusted()
    {
        var context = CreateContext(new ClaimsIdentity());

        ElsaExtensionBuilderApi.ApplyManagementApiKeyPrincipal(context);
        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.Equal("module-management", caller.OwnerId);
        Assert.True(caller.IsTrusted);
    }

    [Fact]
    public void ManagementApiKeyPrincipalRemainsTrustedWhenTrustedRolesAreEmpty()
    {
        var context = CreateContext(new ClaimsIdentity(), new ExtensionBuilderOptions { TrustedRoles = [] });

        ElsaExtensionBuilderApi.ApplyManagementApiKeyPrincipal(context);
        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.Equal("module-management", caller.OwnerId);
        Assert.True(caller.IsTrusted);
    }

    [Fact]
    public void ManagementApiKeyPrincipalPreservesExistingCallerIdentity()
    {
        var context = CreateContext(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "dev"),
            new Claim(ClaimTypes.Name, "Developer")
        ], "test"));

        ElsaExtensionBuilderApi.ApplyManagementApiKeyPrincipal(context);
        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.Equal("dev", caller.OwnerId);
        Assert.True(caller.IsTrusted);
    }

    [Fact]
    public void ApiKeysEqualReturnsFalseForDifferentLengthKeys()
    {
        var matches = ElsaExtensionBuilderApi.ApiKeysEqual("expected-key", "short");

        Assert.False(matches);
    }

    private static DefaultHttpContext CreateContext(ClaimsIdentity identity, ExtensionBuilderOptions? options = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(options ?? new ExtensionBuilderOptions()))
            .BuildServiceProvider();

        return new()
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity)
        };
    }
}
