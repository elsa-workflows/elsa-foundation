using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Qualified_link_recreation_cannot_revive_its_recovery_proof_and_preserves_another_provider_proof_for_the_same_user()
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company"); await MakeAdministrative(host, "partner");
        using var company = host.Browser(); using var partner = host.Browser(); using var fresh = host.Browser();
        var callback = await host.StartAndAuthorize(company);
        using (var completed = await company.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(company)).Subject!; await LinkPartnerToSameUser(host, user);
        var otherCallback = await host.StartAndAuthorize(partner, "partner");
        using (var completed = await partner.GetAsync(otherCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal(user, (await Session(partner)).Subject);
        await using var scope = host.Scope(); var recovery = scope.ServiceProvider.GetRequiredService<IConnectionRecovery>();
        Assert.True(await recovery.IsUsableAsync((await View(host, "company")).Record));
        Assert.True(await recovery.IsUsableAsync((await View(host, "partner")).Record));
        await RemoveAndRecreateCompanyLink(host, user);
        Assert.False(await recovery.IsUsableAsync((await View(host, "company")).Record));
        Assert.True(await recovery.IsUsableAsync((await View(host, "partner")).Record));
        var freshCallback = await host.StartAndAuthorize(fresh);
        using (var completed = await fresh.GetAsync(freshCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.True(await recovery.IsUsableAsync((await View(host, "company")).Record));
    }

    [Fact]
    public async Task Actual_managed_host_coordinates_provider_and_IAM_revisions_without_invalidating_existing_credentials()
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        var user = (await Session(administrator)).Subject!; var pair = await Pair(administrator);
        IamRevisionGuard guard;
        await using (var scope = host.Scope())
            guard = (await scope.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", user))!;
        Assert.NotNull(guard);
        await using (var global = host.Services.CreateAsyncScope())
        {
            global.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test-guarded-provider-write")));
            var store = global.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();
            Assert.True(store.SupportsIdentityRevisionGuards);
            var result = await store.SaveWithIdentityRevisionGuardsAsync(new("guarded-provider-probe", null, "test", true, false,
                ProviderCapabilities.ExternalOidcDefault, new Dictionary<string, string>()), null, [guard]);
            Assert.Equal(IamRevisionSaveStatus.Saved, result.Status);
        }
        Assert.Equal(user, (await Session(administrator)).Subject);
        using (var access = await Bearer(administrator, pair.AccessToken)) Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var refresh = await administrator.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken }))
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        await using var current = host.Scope();
        Assert.NotEqual(guard.Revision, (await current.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", user))!.Revision);
    }

    [Fact]
    public async Task Recovery_observations_require_the_actual_issued_session_for_the_same_user_and_connection()
    {
        await using var host = await HttpHost.Start(); await MakeAdministrative(host, "company");
        using var browser = host.Browser(); using var other = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var completed = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var otherCallback = await host.StartAndAuthorize(other, "partner");
        using (var completed = await other.GetAsync(otherCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(browser)).Subject!;
        var id = new JsonWebToken((await Pair(browser)).AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        var otherId = new JsonWebToken((await Pair(other)).AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        await using (var global = host.Services.CreateAsyncScope())
        {
            global.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test-reset-recovery-observations")));
            var database = global.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>();
            database.GlobalProviderConfigurations.RemoveRange((await database.GlobalProviderConfigurations.ToArrayAsync())
                .Where(row => row.Provider.StartsWith("external-recovery:", StringComparison.Ordinal)));
            await database.SaveChangesAsync();
        }
        await using var scope = host.Scope(); var services = scope.ServiceProvider;
        var view = await View(host, "company");
        var protocol = Assert.Single(services.GetServices<IExternalAuthenticationProtocol>());
        var checkedSource = await protocol.CheckAsync(view.Record);
        var provenance = new ExternalAuthenticationProvenance("company", view.Record.Id, host.Company.Issuer, "opaque-Subject")
        {
            MaterialRevision = view.MaterialRevision, CredentialBinding = checkedSource.CredentialBinding,
            RedirectUri = services.GetRequiredService<IExternalAuthenticationFlowService>().GetCallbackUri("company", AuthenticationArtifactPurpose.SignIn)
        };
        var evidence = services.GetRequiredService<IConnectionSignInEvidence>();
        foreach (var invalid in new[] { null, new string('0', 64), otherId })
        {
            await evidence.RecordAsync(user, provenance with { AuthenticationSessionId = invalid });
            Assert.Empty(await RecoveryDocuments(host));
        }
        await evidence.RecordAsync(user, provenance with { AuthenticationSessionId = id });
        var saved = Assert.Single(await RecoveryDocuments(host));
        Assert.True(await services.GetRequiredService<IConnectionRecovery>().IsUsableAsync(view.Record));
        var sessions = services.GetRequiredService<IAuthenticationSessionStore>();
        var issued = (await sessions.FindAsync("default", id))!;
        Assert.Equal(AuthenticationSessionRevocationStatus.Revoked, await sessions.RevokeAsync("default", id, issued.Revision!, host.Clock.GetUtcNow()));
        host.Clock.Now += TimeSpan.FromMinutes(1);
        await evidence.RecordAsync(user, provenance with { AuthenticationSessionId = id });
        Assert.Equal(saved, Assert.Single(await RecoveryDocuments(host)));
        // Ending an individual session does not remove the credential's ability to sign in again.
        // Its old protected observation remains bounded by the proof window and account version,
        // while a revoked session cannot manufacture a fresh observation.
        Assert.True(await services.GetRequiredService<IConnectionRecovery>().IsUsableAsync(view.Record));
    }

    [Theory]
    [InlineData("rebind")]
    [InlineData("status")]
    [InlineData("security-stamp")]
    public async Task Recovery_evidence_cannot_revive_after_an_account_version_change_without_a_new_normal_sign_in(string change)
    {
        await using var host = await HttpHost.Start(); await MakeAdministrative(host, "company");
        using var browser = host.Browser(); using var fresh = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var completed = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(browser)).Subject!;
        await using var scope = host.Scope();
        var record = (await View(host, "company")).Record;
        var recovery = scope.ServiceProvider.GetRequiredService<IConnectionRecovery>();
        Assert.True(await recovery.IsUsableAsync(record));
        if (change == "rebind")
        {
            await AddLinkTarget(host, "temporary-owner");
            var links = scope.ServiceProvider.GetRequiredService<IRevisionAwareExternalIdentityStore>();
            var key = new ExternalIdentityKey("default", "company", host.Company.Issuer, "opaque-Subject");
            var initial = (await links.FindByKeyWithRevisionAsync(key))!;
            Assert.Equal(IamRevisionSaveStatus.Saved, (await links.SaveWithRevisionAsync(initial.Record with { UserId = "temporary-owner" }, initial.Revision)).Status);
            var moved = (await links.FindByKeyWithRevisionAsync(key))!;
            Assert.Equal(IamRevisionSaveStatus.Saved, (await links.SaveWithRevisionAsync(moved.Record with { UserId = user }, moved.Revision)).Status);
        }
        else if (change == "status")
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            Assert.True(await users.SetStatusAsync("default", user, UserStatus.Disabled));
            Assert.True(await users.SetStatusAsync("default", user, UserStatus.Active));
        }
        else
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            Assert.True((await manager.UpdateSecurityStampAsync((await manager.FindByIdAsync(user))!)).Succeeded);
        }
        Assert.False(await recovery.IsUsableAsync(record));
        var freshCallback = await host.StartAndAuthorize(fresh);
        using (var completed = await fresh.GetAsync(freshCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal(user, (await Session(fresh)).Subject);
        Assert.True(await recovery.IsUsableAsync(record));
    }

    [Theory]
    [InlineData("role", ConnectionReadinessFailure.RoleUnavailable)]
    [InlineData("role-boundary", ConnectionReadinessFailure.RoleForbidden)]
    [InlineData("policy", ConnectionReadinessFailure.PolicyUnavailable)]
    [InlineData("matcher", ConnectionReadinessFailure.PolicyInvalid)]
    [InlineData("protocol", ConnectionReadinessFailure.ProtocolUnavailable)]
    [InlineData("configuration", ConnectionReadinessFailure.ProtocolConfiguration)]
    [InlineData("callback", ConnectionReadinessFailure.CallbackUnavailable)]
    [InlineData("secret", ConnectionReadinessFailure.SecretUnavailable)]
    public async Task Runtime_readiness_reports_closed_configuration_failures_without_provisioning(string invalid, ConnectionReadinessFailure expected)
    {
        await using var host = await HttpHost.Start();
        await using var scope = host.Scope();
        var ready = scope.ServiceProvider.GetRequiredService<IConnectionReadiness>();
        var record = (await View(host, "company")).Record;
        Assert.True((await ready.AssessAsync(record)).Ready);
        var before = await host.Counts();
        record = invalid switch
        {
            "role" => record with { Admission = new() { Type = "jit", DefaultRoleIds = ["missing-role"] } },
            "policy" => record with { Admission = new() { Type = "not-installed" } },
            "matcher" => record with { Admission = new() { Type = "match" } },
            "protocol" => record with { Protocol = "missing" },
            "configuration" => record with { Settings = new Dictionary<string, string>() },
            _ => record
        };
        if (invalid == "callback") host.Options.PublicBaseUrl = null;
        if (invalid == "secret") host.Secrets.Available = false;
        if (invalid == "role-boundary") host.Options.AllowedRoleIds = ["other"];
        Assert.Equal(expected, (await ready.AssessAsync(record)).Failure);
        Assert.Equal(before, await host.Counts());
        Assert.Equal(0, host.Company.TokenRequests);
    }

    [Theory]
    [InlineData("password-method")]
    [InlineData("disabled")]
    [InlineData("membership")]
    [InlineData("permission")]
    [InlineData("password")]
    [InlineData("second-factor")]
    [InlineData("lockout")]
    public async Task A_local_recovery_flag_is_insufficient_without_an_eligible_administrative_password_account(string unavailable)
    {
        await using var host = await HttpHost.Start();
        await using var scope = host.Scope();
        var recovery = scope.ServiceProvider.GetRequiredService<ILocalLoginRecovery>();
        Assert.True(await recovery.IsUsableAsync());
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var admin = (await users.FindByEmailAsync("default", TestAdmin.Email))!;
        switch (unavailable)
        {
            case "password-method": host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false; break;
            case "disabled": Assert.True(await users.SetStatusAsync("default", admin.Id, UserStatus.Disabled)); break;
            case "membership": await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", admin.Id,
                TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>())); break;
            case "permission":
                foreach (var id in admin.RoleIds)
                {
                    var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
                    await roles.SaveAsync((await roles.FindAsync("default", id))! with { Permissions = new HashSet<string>() });
                }
                break;
            default:
                var framework = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
                var user = (await framework.FindByIdAsync(admin.Id))!;
                if (unavailable == "password") Assert.True((await framework.RemovePasswordAsync(user)).Succeeded);
                if (unavailable == "second-factor") Assert.True((await framework.SetTwoFactorEnabledAsync(user, true)).Succeeded);
                if (unavailable == "lockout")
                {
                    Assert.True((await framework.SetLockoutEnabledAsync(user, true)).Succeeded);
                    Assert.True((await framework.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
                }
                break;
        }
        Assert.False(await recovery.IsUsableAsync());
    }

    [Fact]
    public async Task Preview_and_reader_sign_in_do_not_count_as_administrative_recovery_but_a_protected_success_survives_a_new_node()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        await MakeAdministrative(host, "company");
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        await using var request = host.Scope();
        var recovery = request.ServiceProvider.GetRequiredService<IConnectionRecovery>();
        var record = (await View(host, "company")).Record;
        Assert.False(await recovery.IsUsableAsync(record));
        var preview = await host.StartAndAuthorize(administrator, purpose: "preview");
        using (var returned = await administrator.GetAsync(preview)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.False(await recovery.IsUsableAsync(record));
        Assert.Empty(await RecoveryDocuments(host));
        using var reader = host.Browser();
        var readerCallback = await host.StartAndAuthorize(reader, "partner");
        using (var returned = await reader.GetAsync(readerCallback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.False(await recovery.IsUsableAsync((await View(host, "partner")).Record));

        using var normal = host.Browser();
        var normalCallback = await host.StartAndAuthorize(normal);
        using (var returned = await normal.GetAsync(normalCallback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.True(await recovery.IsUsableAsync(record));
        var owner = (await Session(normal)).Subject!;
        var stored = Assert.Single(await RecoveryDocuments(host));
        foreach (var sensitive in new[] { owner, host.Company.Issuer, "opaque-Subject", "CredentialBinding", ControlledOidcServer.ClientSecret })
            Assert.DoesNotContain(sensitive, stored, StringComparison.Ordinal);
        await using var node = host.RuntimeNode();
        var binder = node.GetRequiredService<IPersistenceAccessContextBinder>();
        binder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope("caller-tenant")));
        Assert.True(await node.GetRequiredService<IConnectionRecovery>().IsUsableAsync(record));
        Assert.Equal(new PersistenceScope("caller-tenant"), node.GetRequiredService<IPersistenceAccessContextAccessor>().Current.Scope);
        await using var wrongKeys = host.RuntimeNode("another-application");
        await Assert.ThrowsAsync<InvalidOperationException>(() => wrongKeys.GetRequiredService<IConnectionRecovery>().IsUsableAsync(record).AsTask());
    }

    [Fact]
    public async Task Recovery_proof_survives_presentation_changes_but_refuses_a_tampered_protected_document()
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company");
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        await using var scope = host.Scope();
        var recovery = scope.ServiceProvider.GetRequiredService<IConnectionRecovery>();
        host.Options.Connections[0] = host.Options.Connections[0] with { DisplayName = "Renamed company", DisplayOrder = 12, Preferred = true };
        Assert.True(await recovery.IsUsableAsync((await View(host, "company")).Record));
        await using var global = host.Services.CreateAsyncScope();
        global.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("test-corrupted-recovery-observation")));
        var database = global.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>();
        var document = Assert.Single(await database.GlobalProviderConfigurations.ToArrayAsync(),
            row => row.Provider.StartsWith("external-recovery:", StringComparison.Ordinal));
        var store = global.ServiceProvider.GetRequiredService<IRevisionAwareProviderConfigurationStore>();
        var current = (await store.FindGlobalWithRevisionAsync(document.Provider))!;
        Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(current.Record with
            { Settings = new Dictionary<string, string> { ["protectedProofs"] = "tampered" } }, current.Revision)).Status);
        var record = (await View(host, "company")).Record;
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.IsUsableAsync(record).AsTask());
    }

    [Theory]
    [InlineData("client")]
    [InlineData("secret")]
    [InlineData("callback")]
    [InlineData("expired")]
    [InlineData("disabled-user")]
    [InlineData("membership")]
    [InlineData("permission")]
    [InlineData("link")]
    [InlineData("discovery")]
    public async Task Recovery_evidence_is_rechecked_against_current_configuration_credentials_identity_and_permission(string invalidation)
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company");
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var userId = (await Session(browser)).Subject!;
        await using var scope = host.Scope();
        var recovery = scope.ServiceProvider.GetRequiredService<IConnectionRecovery>();
        Assert.True(await recovery.IsUsableAsync((await View(host, "company")).Record));
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        switch (invalidation)
        {
            case "client": host.Options.Connections[0] = host.Options.Connections[0] with { Settings = new Dictionary<string, string>(host.Options.Connections[0].Settings) { ["clientId"] = "not-registered" } }; break;
            case "secret": host.Secrets.Version++; break;
            case "callback": host.Options.PublicBaseUrl += "/changed"; break;
            case "expired": host.Clock.Now += host.Options.RecoveryEvidenceLifetime + TimeSpan.FromSeconds(1); break;
            case "disabled-user": Assert.True(await users.SetStatusAsync("default", userId, UserStatus.Disabled)); break;
            case "membership": await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", userId,
                TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>())); break;
            case "permission":
                var user = (await users.FindAsync("default", userId))!;
                foreach (var id in user.RoleIds)
                {
                    var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
                    await roles.SaveAsync((await roles.FindAsync("default", id))! with { Permissions = new HashSet<string>() });
                }
                break;
            case "link": await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().ExternalIdentities.Where(row => row.UserId == userId).ExecuteDeleteAsync(); break;
            case "discovery": host.Company.ChangeMetadata = metadata => metadata["issuer"] = host.Company.Issuer + "/changed"; break;
        }
        Assert.False(await recovery.IsUsableAsync((await View(host, "company")).Record));
        Assert.Equal(1, host.Company.TokenRequests); // Recovery checks never authenticate or exchange another code.
    }

    [Fact]
    public async Task An_invalid_baseline_cannot_replace_a_working_override_even_when_a_previous_baseline_sign_in_was_successful()
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company");
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var userId = (await Session(browser)).Subject!;
        var baseline = (await View(host, "company")).Record;
        await using var scope = host.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
        var saved = await store.SaveAsync(new([baseline with { Id = "stored-override", OverridesDeployment = true }]));
        Assert.True(saved.Saved);
        host.Options.AllowDeploymentOverrides = true;
        callback = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var actor = await scope.ServiceProvider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("default", userId));
        var writer = scope.ServiceProvider.GetRequiredService<IConnectionWriter>();
        host.Options.Connections[0] = host.Options.Connections[0] with
            { Settings = new Dictionary<string, string>(baseline.Settings) { ["clientId"] = "unregistered-client" } };
        var request = new ConnectionWrite(ConnectionChange.Archive, saved.Revision, (await View(host, "company")).Record);
        Assert.Equal("final-login-path", (await writer.ApplyAsync(request, actor)).Failure);
        Assert.Equal("stored-override", (await View(host, "company")).Record.Id);
        // Restoring the actual proved baseline makes the same source transition safe.
        host.Options.Connections[0] = baseline;
        Assert.True((await writer.ApplyAsync(request, actor)).Saved);
        Assert.Equal("deployment:company", (await View(host, "company")).Record.Id);
        Assert.Equal(2, host.Company.TokenRequests);
    }

    [Fact]
    public async Task Concurrent_real_catalog_writers_cannot_both_remove_their_last_proved_administrative_paths()
    {
        await using var host = await HttpHost.Start();
        await MakeAdministrative(host, "company"); await MakeAdministrative(host, "partner");
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        using var company = host.Browser(); using var partner = host.Browser();
        var first = await host.StartAndAuthorize(company); using (var returned = await company.GetAsync(first)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var second = await host.StartAndAuthorize(partner, "partner"); using (var returned = await partner.GetAsync(second)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var actorId = (await Session(company)).Subject!;
        // Persist full enabled records with the same IDs/material that the two real normal sign-ins proved.
        // Moving a source to a new ID or material would require a new successful sign-in.
        await using (var scope = host.Scope())
        {
            var current = (await scope.ServiceProvider.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections.Select(view => view.Record with { Id = "stored:" + view.Record.Key }).ToArray();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().SaveAsync(new(current))).Saved);
        }
        host.Options.Connections.Clear();
        // Sign in under the stored records, so proof belongs to their actual source identities.
        first = await host.StartAndAuthorize(company); using (var returned = await company.GetAsync(first)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        second = await host.StartAndAuthorize(partner, "partner"); using (var returned = await partner.GetAsync(second)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        await using var native = host.Scope();
        var inner = native.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
        var catalog = await inner.ReadAsync();
        var barrier = new RecoveryReadBarrier();
        await using var nodeA = host.RuntimeNode(catalog: new RecoverySynchronizedCatalog(inner, barrier));
        await using var nodeB = host.RuntimeNode(catalog: new RecoverySynchronizedCatalog(inner, barrier));
        await using var a = nodeA.CreateAsyncScope(); await using var b = nodeB.CreateAsyncScope();
        a.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        b.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var principal = await a.ServiceProvider.GetRequiredService<IPrincipalFactory>().CreateAsync(new("default", actorId));
        var one = catalog.Records.Single(record => record.Key == "company"); var two = catalog.Records.Single(record => record.Key == "partner");
        var outcomes = await Task.WhenAll(
            a.ServiceProvider.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Disable, catalog.Revision, one), principal).AsTask(),
            b.ServiceProvider.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Disable, catalog.Revision, two), principal).AsTask());
        Assert.Single(outcomes, outcome => outcome.Saved);
        Assert.Contains(Assert.Single(outcomes, outcome => !outcome.Saved).Failure, new[] { "revision-conflict", "identity-authority-changed" });
        Assert.Equal(2, barrier.Reads);
        var latest = await a.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().ReadAsync();
        var remaining = Assert.Single(latest.Records, record => record.Enabled);
        var denied = await a.ServiceProvider.GetRequiredService<IConnectionWriter>().ApplyAsync(new(ConnectionChange.Disable, latest.Revision, remaining), principal);
        Assert.Equal("final-login-path", denied.Failure);
        Assert.Single((await a.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().ReadAsync()).Records, record => record.Enabled);
    }

    private static async Task MakeAdministrative(HttpHost host, string key)
    {
        await using var scope = host.Scope();
        var admin = (await scope.ServiceProvider.GetRequiredService<IUserStore>().FindByEmailAsync("default", TestAdmin.Email))!;
        var index = host.Options.Connections.FindIndex(record => record.Key == key);
        host.Options.Connections[index] = host.Options.Connections[index] with { Admission = new() { Type = "jit", DefaultRoleIds = admin.RoleIds.ToArray() } };
    }
    private static async Task<ConnectionView> View(HttpHost host, string key)
    {
        await using var scope = host.Scope();
        return Assert.Single((await scope.ServiceProvider.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections, view => view.Record.Key == key && view.Effective);
    }
    private static async Task<string[]> RecoveryDocuments(HttpHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Global);
        // Provider identity columns use the exact UTF-16 codec; filter the decoded fixture values in memory.
        var documents = (await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().GlobalProviderConfigurations.ToArrayAsync())
            .Where(row => row.Provider.StartsWith("external-recovery:", StringComparison.Ordinal));
        var store = scope.ServiceProvider.GetRequiredService<IProviderConfigurationStore>();
        var payloads = new List<string>();
        foreach (var document in documents)
            payloads.Add((await store.FindGlobalAsync(document.Provider))!.Settings["protectedProofs"]);
        return payloads.ToArray();
    }
    private sealed class RecoveryReadBarrier
    {
        public int Reads;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Arrive(CancellationToken token)
        {
            var read = Interlocked.Increment(ref Reads);
            if (read > 2) return;
            if (read == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(token);
        }
    }
    private sealed class RecoverySynchronizedCatalog(IConnectionCatalogStore inner, RecoveryReadBarrier barrier) : IConnectionCatalogStore
    {
        public bool SupportsIdentityRevisionGuards => inner.SupportsIdentityRevisionGuards;
        public async ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await inner.ReadAsync(cancellationToken);
            await barrier.Arrive(cancellationToken);
            return snapshot;
        }
        public ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default) => inner.SaveAsync(catalog, cancellationToken);
    }
}
