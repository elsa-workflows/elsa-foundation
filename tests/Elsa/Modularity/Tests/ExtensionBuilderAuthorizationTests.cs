using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
    public void ManagementApiKeyPrincipalDoesNotTrustAuthenticatedUnlistedRole()
    {
        var context = CreateContext(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "dev"),
            new Claim(ClaimTypes.Name, "Developer")
        ], "test"));

        ElsaExtensionBuilderApi.ApplyManagementApiKeyPrincipal(context);
        var caller = ElsaExtensionBuilderApi.CreateCaller(context, hasManagementAccess: true);

        Assert.Equal("dev", caller.OwnerId);
        Assert.False(caller.IsTrusted);
    }

    [Fact]
    public void ApiKeysEqualReturnsFalseForDifferentLengthKeys()
    {
        var matches = ElsaExtensionBuilderApi.ApiKeysEqual("expected-key", "short");

        Assert.False(matches);
    }

    [Fact]
    public async Task PromotionRequestReadsCamelCaseJson()
    {
        var context = CreatePromotionRequestContext("""{"targetFeed":"requested"}""", contentLength: 26);

        var request = await ElsaExtensionBuilderApi.ReadPromotionRequestAsync(context, CancellationToken.None);

        Assert.Equal("requested", request.TargetFeed);
    }

    [Fact]
    public async Task PromotionRequestReadsStreamedJsonWithoutContentLength()
    {
        var context = CreatePromotionRequestContext("""{"targetFeed":"streamed"}""", contentLength: null);

        var request = await ElsaExtensionBuilderApi.ReadPromotionRequestAsync(context, CancellationToken.None);

        Assert.Equal("streamed", request.TargetFeed);
    }

    [Fact]
    public async Task PromotionRequestRejectsMalformedJson()
    {
        var context = CreatePromotionRequestContext("""{"targetFeed":""", contentLength: 14);

        await Assert.ThrowsAsync<JsonException>(() => ElsaExtensionBuilderApi.ReadPromotionRequestAsync(context, CancellationToken.None));
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

    private static DefaultHttpContext CreatePromotionRequestContext(string json, long? contentLength)
    {
        var context = CreateContext(new ClaimsIdentity());
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = contentLength;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context;
    }
}
