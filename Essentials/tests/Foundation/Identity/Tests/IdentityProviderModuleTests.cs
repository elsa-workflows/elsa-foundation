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
    public async Task PrincipalFactory_projects_only_the_admitted_canonical_user_and_saved_Elsa_roles()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityApi();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        await SeedCanonicalUser(provider);
        await provider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "tenant-a", "Reader", null,
            new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }, false));
        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("tenant-a", "user-ada", "company"));
        var session = await provider.GetRequiredService<IAuthSessionService>().GetAsync(principal);
        Assert.Equal("authenticated", session.Status);
        Assert.Equal("user-ada", session.Subject);
        Assert.Equal("Ada Lovelace", session.DisplayName);
        Assert.Equal("tenant-a", session.TenantId);
        Assert.Equal("company", session.Provider);
        Assert.Equal(["reader"], session.Roles);
        Assert.Equal([DefaultIdentityPermissionKeys.IdentityUsersRead], session.Permissions);
        Assert.Equal("user-ada", Assert.Single(principal.FindAll(ClaimTypes.NameIdentifier)).Value);
        Assert.Equal("user-ada", Assert.Single(principal.FindAll("sub")).Value);
        Assert.Equal("user-ada", Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
        Assert.Empty(await provider.GetRequiredService<IExternalIdentityStore>().ListForUserAsync("tenant-a", "user-ada"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("suspended")]
    public async Task PrincipalFactory_refuses_unavailable_users_without_automatic_provisioning(string state)
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        using var provider = services.BuildServiceProvider();
        if (state != "missing") await SeedCanonicalUser(provider);
        if (state == "disabled") await provider.GetRequiredService<IUserStore>().SetStatusAsync("tenant-a", "user-ada", UserStatus.Disabled);
        if (state == "suspended") await provider.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("tenant-a", "user-ada", TenantMembershipStatus.Suspended,
            new HashSet<string>(), new HashSet<string>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("tenant-a", "user-ada")).AsTask());
        Assert.Empty(await provider.GetRequiredService<IExternalIdentityStore>().ListForUserAsync("tenant-a", "user-ada"));
        Assert.Null(await provider.GetRequiredService<IUserStore>().FindByEmailAsync("tenant-a", "unknown@example.test"));
    }

    [Fact]
    public async Task PrincipalFactory_emits_a_trusted_marker_without_running_external_claim_normalization()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentity();
        services.AddScoped<IClaimsNormalizer, ThrowingClaimsNormalizer>();
        using var provider = services.BuildServiceProvider();
        await SeedCanonicalUser(provider);
        var principal = await provider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("tenant-a", "user-ada"));
        var identity = Assert.Single(principal.Identities);
        Assert.Equal("Elsa.Foundation.Identity", identity.AuthenticationType);
        Assert.Equal("v1", Assert.Single(identity.FindAll(IdentityClaimTypes.Normalized)).Value);
        Assert.True(provider.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(principal, out _));
    }

    private static ValueTask SeedCanonicalUser(IServiceProvider provider) => provider.GetRequiredService<IUserStore>().SaveAsync(new(
        "user-ada", "tenant-a", "ada", "ada@example.test", "Ada Lovelace", UserStatus.Active, ResourceOwnership.Foundation,
        new HashSet<string> { "reader" }, new HashSet<string>()));

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

        var issued = await tokenService.IssueAsync(new TokenIssueRequest(new TokenSubject("tenant-a", "user-1"), [DefaultIdentityPermissionKeys.IdentityUsersRead]));
        var validated = await tokenService.ValidateAsync(new TokenValidationRequest(issued.AccessToken));
        Assert.True(validated.Succeeded, validated.Failure);
        var session = await sessionService.GetAsync(Assert.IsType<ClaimsPrincipal>(validated.Principal));
        var refreshed = await tokenService.RefreshAsync(new TokenRefreshRequest(issued.RefreshToken!));
        var refreshedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));
        await tokenService.RevokeAsync(new TokenRevocationRequest(refreshed.AccessToken));
        var revokedValidation = await tokenService.ValidateAsync(new TokenValidationRequest(refreshed.AccessToken));

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
        var principal = await principalFactory.CreateAsync(new PrincipalFactoryContext("tenant-a", "user-42", "entra"));

        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(c => c.Value).ToList();
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions);
        Assert.Contains(principal.FindAll(IdentityClaimTypes.Role), c => c.Value == "role-editor");
    }

    private sealed class ThrowingClaimsNormalizer : IClaimsNormalizer
    {
        public ValueTask<ClaimsNormalizationResult> NormalizeAsync(
            ClaimsNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ClaimsNormalizationResult>(new InvalidOperationException("normalization failed"));
    }
}
