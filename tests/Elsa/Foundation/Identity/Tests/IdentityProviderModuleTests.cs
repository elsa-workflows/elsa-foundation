using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class IdentityProviderModuleTests
{
    [Fact]
    public void FeatureClassesRegisterOwnedServices()
    {
        var services = new ServiceCollection();

        new FoundationIdentityApiFeature().ConfigureServices(services);
        new AspNetCoreIdentityFeature().ConfigureServices(services);
        new OidcAuthenticationFeature().ConfigureServices(services);
        services.AddOpenIddictVendorForTests(
            builder => builder.UseInMemoryDatabase($"openiddict-{Guid.NewGuid():n}"));
        new OpenIddictIdentityFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthSessionService>());
        Assert.NotNull(provider.GetRequiredService<IPrincipalFactory>());
        Assert.NotNull(provider.GetRequiredService<ITokenService>());
        Assert.Contains(provider.GetServices<IAuthenticationProviderModule>(), x => x.ProviderId == "oidc");
        Assert.Contains(provider.GetServices<IAuthenticationProviderModule>(), x => x.ProviderId == "openiddict");
    }

    [Fact]
    public async Task OidcAndOpenIddictProvidersAreExposedThroughSameProviderManager()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityOidc(options =>
        {
            options.ProviderId = "entra";
            options.DisplayName = "Microsoft Entra";
            options.AuthenticationScheme = "entra";
            options.ChallengePath = "/_elsa/identity/challenge/entra";
            options.IsDefault = true;
            // A ClientId makes the interactive handler available, so the provider advertises its challenge.
            // Without one it is validation-only and (correctly) surfaces no interactive challenge.
            options.ClientId = "entra-client";
            options.Authority = "https://login.example.com/";
            options.RequireHttpsMetadata = false;
        });
        services.AddFoundationIdentityOpenIddict(options =>
        {
            options.ProviderId = "local";
            options.DisplayName = "Local identity";
        });

        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IAuthenticationProviderResolver>();

        var providers = await manager.ListAsync();

        Assert.Collection(
            providers,
            x =>
            {
                Assert.Equal("entra", x.Id);
                Assert.Equal("external-oidc", x.Kind);
                Assert.Equal("/_elsa/identity/challenge/entra", x.Challenge?.Url);
            },
            x =>
            {
                Assert.Equal("local", x.Id);
                Assert.Equal("openiddict", x.Kind);
                Assert.True(x.Capabilities.SupportsTokenIssuance);
            });
    }

    [Fact]
    public async Task PrincipalFactoryLinksExternalIdentityAndProducesNormalizedSession()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityApi();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        var principalFactory = provider.GetRequiredService<IPrincipalFactory>();
        var sessionService = provider.GetRequiredService<IAuthSessionService>();
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Ada Lovelace"),
            new Claim(ClaimTypes.Email, "ada@example.com"),
            new Claim("groups", "admins")
        ], "external"));

        var principal = await principalFactory.CreateAsync(new PrincipalFactoryContext(
            "tenant-a",
            "entra",
            "external-ada",
            externalPrincipal,
            [
                new ClaimMappingRule("admins", "tenant-a", "entra", "groups", "admins", new HashSet<string> { "admin" }, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }, 1, false)
            ],
            []));
        var session = await sessionService.GetAsync(principal);

        Assert.Equal("authenticated", session.Status);
        Assert.NotNull(session.Subject);
        Assert.Equal("Ada Lovelace", session.DisplayName);
        Assert.Equal("tenant-a", session.TenantId);
        Assert.Equal("entra", session.Provider);
        Assert.Contains("admin", session.Roles);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, session.Permissions);

        var externalIdentities = provider.GetRequiredService<IExternalIdentityStore>();
        var linkedIdentity = await externalIdentities.FindBySubjectAsync("tenant-a", "entra", "external-ada");
        Assert.Equal(session.Subject, linkedIdentity?.UserId);
    }

    [Fact]
    public async Task ExternalPrincipalFactory_EmitsTrustedMarkerAfterProjection()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();

        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(
            CreateContext("entra", "external-ada", "ada@example.com"));
        var identity = Assert.Single(principal.Identities);

        Assert.Equal("Elsa.Foundation.Identity", identity.AuthenticationType);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.True(provider.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(principal, out _));
    }

    [Fact]
    public async Task ExternalPrincipalFactory_PropagatesNormalizationFailureWithoutReturningPrincipal()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        services.AddScoped<IClaimsNormalizer, ThrowingClaimsNormalizer>();
        using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(
                CreateContext("entra", "external-ada", "ada@example.com")));

        Assert.Equal("normalization failed", exception.Message);
    }

    [Fact]
    public async Task DuplicateEmailAcrossProvidersCreatesSeparateLinkedUsers()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        var principalFactory = provider.GetRequiredService<IPrincipalFactory>();

        var first = await principalFactory.CreateAsync(CreateContext("entra", "entra-subject", "same@example.com"));
        var second = await principalFactory.CreateAsync(CreateContext("github", "github-subject", "same@example.com"));

        Assert.NotEqual(
            first.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            second.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task OpenIddictTokenServiceIssuesRefreshesValidatesAndRevokesContractTokens()
    {
        // Exercises the REAL OpenIddict pipeline (JWT issuance + local validation + EF token store):
        // development/demo mode self-provisions an in-memory store and ephemeral keys.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOpenIddict(options =>
        {
            options.ProviderId = "local";
            options.IsDevelopmentOrDemo = true;
        });
        services.AddOpenIddictVendorForTests(
            builder => builder.UseInMemoryDatabase($"openiddict-{Guid.NewGuid():n}"));
        services.AddFoundationIdentityApi();
        using var provider = services.BuildServiceProvider();

        var tokenService = provider.GetRequiredService<ITokenService>();
        var sessionService = provider.GetRequiredService<IAuthSessionService>();

        var issued = await tokenService.IssueAsync(new TokenIssueRequest("user-1", "tenant-a", [DefaultIdentityPermissionKeys.IdentityUsersRead]));
        var validated = await tokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));
        var session = await sessionService.GetAsync(validated.Principal!);
        var refreshed = await tokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));
        var refreshedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));
        await tokenService.RevokeAsync(new TokenRevocationRequest(refreshed.AccessToken));
        var revokedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));

        Assert.True(validated.Succeeded);
        Assert.Equal("authenticated", session.Status);
        Assert.Equal("user-1", session.Subject);
        Assert.Equal("tenant-a", session.TenantId);
        Assert.Equal("local", session.Provider);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, session.Permissions);
        Assert.True(refreshedValidation.Succeeded);
        Assert.False(revokedValidation.Succeeded);
    }

    [Fact]
    public async Task PrincipalFactoryExpandsRoleGrantedPermissions()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();

        var roles = provider.GetRequiredService<IRoleStore>();
        var users = provider.GetRequiredService<IUserStore>();
        var externalIdentities = provider.GetRequiredService<IExternalIdentityStore>();

        // A role that carries a permission, and a user in that role with NO direct permissions.
        await roles.SaveAsync(new RoleRecord(
            "role-editor",
            "tenant-a",
            "Editor",
            null,
            new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage },
            System: false));

        await users.SaveAsync(new UserRecord(
            "user-42",
            "tenant-a",
            "editor@example.com",
            "editor@example.com",
            "Role Editor",
            UserStatus.Active,
            ResourceOwnership.External,
            new HashSet<string> { "role-editor" },
            new HashSet<string>()));

        await externalIdentities.SaveAsync(new ExternalIdentityRecord(
            "tenant-a",
            "entra",
            "editor-subject",
            "user-42",
            DateTimeOffset.UnixEpoch,
            null,
            ExternalIdentityLinkPolicy.Auto));

        var principalFactory = provider.GetRequiredService<IPrincipalFactory>();
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Role Editor"),
            new Claim(ClaimTypes.Email, "editor@example.com")
        ], "entra"));

        var principal = await principalFactory.CreateAsync(new PrincipalFactoryContext(
            "tenant-a",
            "entra",
            "editor-subject",
            externalPrincipal,
            [],
            []));

        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(c => c.Value).ToList();
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions);
        Assert.Contains(principal.FindAll(IdentityClaimTypes.Role), c => c.Value == "role-editor");
    }

    private static PrincipalFactoryContext CreateContext(string provider, string providerSubject, string email)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, providerSubject),
            new Claim(ClaimTypes.Email, email)
        ], provider));

        return new PrincipalFactoryContext("tenant-a", provider, providerSubject, principal, [], []);
    }

    private sealed class ThrowingClaimsNormalizer : IClaimsNormalizer
    {
        public ValueTask<ClaimsNormalizationResult> NormalizeAsync(
            ClaimsNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ClaimsNormalizationResult>(new InvalidOperationException("normalization failed"));
    }
}
