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
        Assert.True(anonymousWithManagementKey.IsTrusted);
        Assert.False(developer.IsTrusted);
    }

    private static DefaultHttpContext CreateContext(ClaimsIdentity identity)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new ExtensionBuilderOptions()))
            .BuildServiceProvider();

        return new()
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity)
        };
    }
}
