using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.Oidc.Extensions;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class IdentityProviderModuleTests
{
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
    public async Task PrincipalFactoryProjectsStoredRolesWithoutProvisioningAnExternalLink()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityApi();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IRoleStore>().SaveAsync(new RoleRecord(
            "admin", "tenant-a", "Admin", null, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }, false));
        await provider.GetRequiredService<IUserStore>().SaveAsync(new UserRecord(
            "ada", "tenant-a", "ada", "ada@example.com", "Ada Lovelace", UserStatus.Active, ResourceOwnership.External,
            new HashSet<string> { "admin" }, new HashSet<string>()));
        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("tenant-a", "ada", "entra"));
        var session = await provider.GetRequiredService<IAuthSessionService>().GetAsync(principal);

        Assert.Equal("authenticated", session.Status);
        Assert.Equal("ada", session.Subject);
        Assert.Equal("Ada Lovelace", session.DisplayName);
        Assert.Equal("tenant-a", session.TenantId);
        Assert.Equal("entra", session.Provider);
        Assert.Contains("admin", session.Roles);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersRead, session.Permissions);
        Assert.Empty(await provider.GetRequiredService<IExternalIdentityStore>().ListForUserAsync("tenant-a", "ada"));
    }

    [Fact]
    public async Task ExternalOriginContextNeverReplacesTheCanonicalElsaUserId()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        var context = await SavedContextAsync(provider, "entra", "canonical-ada", "ada@example.com");
        context = context with { ExternalIdentity = new ExternalIdentityKey("tenant-a", "entra", "https://validated.example/", "provider-subject-ada") };
        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(context);
        Assert.Equal("canonical-ada", Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
        Assert.Equal("canonical-ada", Assert.Single(principal.FindAll(ClaimTypes.NameIdentifier)).Value);
        Assert.Equal("canonical-ada", Assert.Single(principal.FindAll("sub")).Value);
    }

    [Fact]
    public async Task ExternalPrincipalFactory_EmitsTrustedMarkerAfterProjection()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();

        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(
            await SavedContextAsync(provider, "entra", "external-ada", "ada@example.com"));
        var identity = Assert.Single(principal.Identities);

        Assert.Equal("Elsa.Foundation.Identity", identity.AuthenticationType);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.True(provider.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(principal, out _));
    }

    [Fact]
    public async Task ExternalPrincipalFactoryRefusesAnUnadmittedUserWithoutProvisioning()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("tenant-a", "missing", "entra")));
        Assert.Null(await provider.GetRequiredService<IUserStore>().FindAsync("tenant-a", "missing"));
    }

    [Fact]
    public async Task DuplicateSavedEmailsDoNotMergeExplicitCanonicalUsers()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        var principalFactory = provider.GetRequiredService<IPrincipalFactory>();

        var first = await principalFactory.CreateAsync(await SavedContextAsync(provider, "entra", "entra-user", "same@example.com"));
        var second = await principalFactory.CreateAsync(await SavedContextAsync(provider, "github", "github-user", "same@example.com"));

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
        var databaseName = $"openiddict-{Guid.NewGuid():n}";
        services.AddLogging();
        services.AddOpenIddictVendorForTests(
            builder => builder.UseInMemoryDatabase(databaseName));
        services.AddFoundationIdentityOpenIddict(options =>
        {
            options.ProviderId = "local";
            options.IsDevelopmentOrDemo = true;
        });
        services.AddFoundationIdentityApi();
        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();

        var tokenService = provider.GetRequiredService<ITokenService>();
        var sessionService = provider.GetRequiredService<IAuthSessionService>();

        var issued = await tokenService.IssueAsync(new TokenIssueRequest("user-1", "tenant-a", [DefaultIdentityPermissionKeys.IdentityUsersRead]) { Provider = "local", SubjectIssuer = "https://validated.example/" });
        var validated = await tokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));
        Assert.True(validated.Succeeded, validated.Failure);
        var session = await sessionService.GetAsync(Assert.IsType<ClaimsPrincipal>(validated.Principal));
        var refreshed = await tokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));
        var refreshedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));
        await tokenService.RevokeAsync(new TokenRevocationRequest(refreshed.AccessToken));
        var revokedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));

        Assert.Equal("authenticated", session.Status);
        Assert.StartsWith("elsa:v2:provider:", session.Subject);
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
        var principal = await principalFactory.CreateAsync(new PrincipalFactoryContext("tenant-a", "user-42", "entra"));

        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(c => c.Value).ToList();
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions);
        Assert.Contains(principal.FindAll(IdentityClaimTypes.Role), c => c.Value == "role-editor");
    }

    private static async Task<PrincipalFactoryContext> SavedContextAsync(IServiceProvider provider, string authority, string userId, string email)
    {
        await provider.GetRequiredService<IUserStore>().SaveAsync(new UserRecord(
            userId, "tenant-a", userId, email, userId, UserStatus.Active, ResourceOwnership.External,
            new HashSet<string>(), new HashSet<string>()));
        return new PrincipalFactoryContext("tenant-a", userId, authority);
    }
}
