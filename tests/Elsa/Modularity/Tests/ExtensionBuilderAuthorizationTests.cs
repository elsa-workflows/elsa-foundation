using System.Security.Claims;
using Elsa.Server;
using Elsa.Server.ExtensionBuilder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
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
    public void ManagementApiKeyValidateReturnsNotFoundWhenNoKeyConfigured()
    {
        var context = CreateContextWithConfiguration(configuredApiKey: null, providedApiKey: "anything");

        var result = ManagementApiKeyAuthentication.Validate(context);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public void ManagementApiKeyValidateReturnsUnauthorizedWhenKeyMissingOrWrong()
    {
        var missing = ManagementApiKeyAuthentication.Validate(CreateContextWithConfiguration("expected-key", providedApiKey: null));
        var wrong = ManagementApiKeyAuthentication.Validate(CreateContextWithConfiguration("expected-key", providedApiKey: "wrong-key"));

        Assert.IsType<UnauthorizedHttpResult>(missing);
        Assert.IsType<UnauthorizedHttpResult>(wrong);
    }

    [Fact]
    public void ManagementApiKeyValidateReturnsNullWhenKeyMatches()
    {
        var result = ManagementApiKeyAuthentication.Validate(CreateContextWithConfiguration("expected-key", providedApiKey: "expected-key"));

        Assert.Null(result);
    }

    [Fact]
    public void ManagementApiKeyKeysEqualUsesConstantTimeComparison()
    {
        Assert.True(ManagementApiKeyAuthentication.KeysEqual("secret", "secret"));
        Assert.False(ManagementApiKeyAuthentication.KeysEqual("secret", "Secret"));
        Assert.False(ManagementApiKeyAuthentication.KeysEqual("secret", "secre"));
    }

    private static DefaultHttpContext CreateContextWithConfiguration(string? configuredApiKey, string? providedApiKey)
    {
        var settings = new Dictionary<string, string?>();
        if (configuredApiKey is not null)
            settings[ManagementApiKeyAuthentication.ConfigurationKey] = configuredApiKey;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        if (providedApiKey is not null)
            context.Request.Headers[ManagementApiKeyAuthentication.HeaderName] = providedApiKey;

        return context;
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
