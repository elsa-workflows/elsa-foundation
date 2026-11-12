using System.Security.Claims;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests;

public sealed class ExternalAdmissionPolicyTests
{
    [Fact]
    public async Task Unknown_identity_is_rejected_by_default_and_assessment_never_writes()
    {
        await using var host = Create("reject");
        var result = await host.Service.AssessAsync(await host.Context());
        Assert.Equal(ExternalAdmissionFailure.UnknownIdentity, result.Failure);
        Assert.Equal(ExternalAdmissionFailure.UnknownIdentity, (await host.Service.AdmitAsync(await host.Context())).Failure);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task Jit_preview_projects_existing_Elsa_roles_ignores_upstream_grants_and_admission_saves_once()
    {
        await using var host = Create("jit", ["reader"]);
        await host.Role("reader", "workflows:read");
        var context = await host.Context();
        context = context with { Claims = context.Claims.Concat(new[] { new Claim(ClaimTypes.Role, "admin"), new Claim(IdentityClaimTypes.Permission, "*") }).ToArray() };
        var assessment = await host.Service.AssessAsync(context);
        Assert.Equal(ExternalAdmissionKind.NewUser, assessment.Kind);
        Assert.Equal("", assessment.User!.Id);
        Assert.Equal(["reader"], assessment.RoleIds);
        Assert.Equal(["workflows:read"], assessment.Permissions);
        Assert.Equal(0, host.Writes);
        Assert.Null(await host.Users.FindByEmailAsync("default", "ada@example.test"));
        var admitted = await host.Service.AdmitAsync(context);
        Assert.Equal(ExternalAdmissionFailure.None, admitted.Failure);
        var user = (await host.Users.FindAsync("default", admitted.UserId!))!;
        Assert.Equal(["reader"], user.RoleIds);
        Assert.Empty(user.DirectPermissions);
        Assert.StartsWith("external-", user.UserName, StringComparison.Ordinal);
        Assert.Equal(1, host.Writes);
        Assert.Equal(admitted.UserId, (await host.Service.AdmitAsync(context)).UserId);
        Assert.Equal(1, host.Writes);
    }

    [Fact]
    public async Task Integrator_provisioning_assigns_saved_roles_once_and_later_sign_ins_preserve_manual_memberships()
    {
        var policy = new CustomPolicy("integrator", ["reader"]);
        await using var host = Create("integrator", ["reader"], custom: policy);
        await host.Role("reader", "workflows:read");
        await host.Role("operator", "workflows:execute");
        var initial = await host.Service.AdmitAsync(await host.Context());
        Assert.Equal(ExternalAdmissionFailure.None, initial.Failure);
        var saved = (await host.Users.FindAsync("default", initial.UserId!))!;
        Assert.Equal(["reader"], saved.RoleIds);
        Assert.Equal(1, policy.Calls);
        await host.Users.SaveAsync(saved with { RoleIds = new HashSet<string> { "operator" } });
        host.Reset();
        var context = await host.Context();
        context = context with { Claims = context.Claims.Concat(new[] {
            new Claim("groups", "admin"), new Claim(ClaimTypes.Role, "admin"), new Claim(IdentityClaimTypes.Permission, "*")
        }).ToArray() };
        var preview = await host.Service.AssessAsync(context);
        Assert.Equal(["operator"], preview.RoleIds);
        Assert.Equal(["workflows:execute"], preview.Permissions);
        Assert.Equal(initial.UserId, (await host.Service.AdmitAsync(context)).UserId);
        Assert.Equal(["operator"], (await host.Users.FindAsync("default", initial.UserId!))!.RoleIds);
        Assert.Equal(1, policy.Calls);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task Explicit_Jit_for_two_providers_creates_distinct_users_even_when_their_email_is_equal()
    {
        await using var host = Create("jit");
        var options = host.Provider.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        options.Connections.Add(options.Connections[0] with { Key = "second" });
        var connections = (await host.Provider.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections;
        var claims = new Claim[] { new("iss", "https://issuer/"), new("sub", "subject"), new("email", "same@example.test") };
        var first = await host.Service.AdmitAsync(new(connections.Single(view => view.Record.Key == "company"), new("default", "company", "https://issuer/", "subject"), claims));
        var second = await host.Service.AdmitAsync(new(connections.Single(view => view.Record.Key == "second"), new("default", "second", "https://issuer/", "subject"), claims));
        Assert.Equal(ExternalAdmissionFailure.None, first.Failure);
        Assert.Equal(ExternalAdmissionFailure.None, second.Failure);
        Assert.NotEqual(first.UserId, second.UserId);
        Assert.Null(await host.Users.FindByEmailAsync("default", "same@example.test"));
    }

    [Fact]
    public async Task Verified_email_matching_is_explicit_unique_and_does_not_change_manual_roles()
    {
        await using var host = Create("match", ["reader"], "verified-email");
        await host.Role("reader", "workflows:read");
        var user = User("ada", ["reader"]);
        await host.Users.SaveAsync(user);
        host.Reset();
        var context = await host.Context(verified: true);
        var preview = await host.Service.AssessAsync(context);
        Assert.Equal(ExternalAdmissionKind.ExistingUser, preview.Kind);
        Assert.Equal(0, host.Writes);
        var result = await host.Service.AdmitAsync(context);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(user.RoleIds, (await host.Users.FindAsync("default", user.Id))!.RoleIds);
        Assert.Equal(user.Id, (await host.Links.FindByKeyAsync(context.Identity))!.UserId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("string")]
    [InlineData("duplicate")]
    [InlineData("false")]
    public async Task Matcher_requires_one_verified_boolean_email_claim(string evidence)
    {
        await using var host = Create("match", [], "verified-email");
        await host.Users.SaveAsync(User("ada", []));
        host.Reset();
        var context = await host.Context();
        var claims = context.Claims.ToList();
        if (evidence != "missing") claims.Add(new("email_verified", evidence == "false" ? "false" : "true", evidence == "string" ? ClaimValueTypes.String : ClaimValueTypes.Boolean));
        if (evidence == "duplicate") claims.Add(new("email_verified", "true", ClaimValueTypes.Boolean));
        var result = await host.Service.AssessAsync(context with { Claims = claims });
        Assert.Equal(ExternalAdmissionFailure.VerifiedEmailRequired, result.Failure);
        Assert.Equal(0, host.Writes);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("disabled")]
    [InlineData("external-profile")]
    [InlineData("missing")]
    public async Task Matcher_refuses_ineligible_addresses_without_Jit_fallback(string state)
    {
        await using var host = Create("match", [], "verified-email");
        if (state != "missing") await host.Users.SaveAsync(User("ada", []) with
        {
            Status = state == "disabled" ? UserStatus.Disabled : UserStatus.Active,
            Ownership = state == "external-profile" ? ResourceOwnership.External : ResourceOwnership.Foundation
        });
        if (state == "ambiguous") await host.Users.SaveAsync(User("another", []));
        host.Reset();
        var result = await host.Service.AdmitAsync(await host.Context(verified: true));
        Assert.Equal(ExternalAdmissionFailure.NoEligibleMatch, result.Failure);
        Assert.Null(result.UserId);
        Assert.Equal(0, host.Writes);
    }

    [Theory]
    [InlineData(OwnershipMode.ExternalOwned)]
    [InlineData(OwnershipMode.Hybrid)]
    public async Task Ownership_prevents_local_provisioning_but_does_not_block_an_existing_link(OwnershipMode mode)
    {
        await using var host = Create("jit", mode: mode);
        var context = await host.Context();
        Assert.Equal(ExternalAdmissionFailure.OwnershipDenied, (await host.Service.AssessAsync(context)).Failure);
        await host.Users.SaveAsync(User("ada", []));
        await host.Links.SaveAsync(new("default", "company", "subject", "ada", DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Admin) { Issuer = "https://issuer/" });
        host.Reset();
        Assert.Equal("ada", (await host.Service.AdmitAsync(context)).UserId);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task An_authority_without_atomic_admission_can_resolve_known_links_but_cannot_promise_provisioning()
    {
        await using var host = Create("jit");
        var store = Assert.IsType<TrackingStore>(host.Links);
        store.SupportsAtomicAdmission = false;
        Assert.Equal(ExternalAdmissionFailure.IdentityUnavailable, (await host.Service.AssessAsync(await host.Context())).Failure);
        await host.Users.SaveAsync(User("ada", []));
        await host.Links.SaveAsync(new("default", "company", "subject", "ada", DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Admin) { Issuer = "https://issuer/" });
        Assert.Equal("ada", (await host.Service.AdmitAsync(await host.Context())).UserId);
        store.SupportsQualifiedKeys = false;
        Assert.Equal(ExternalAdmissionFailure.IdentityUnavailable, (await host.Service.AssessAsync(await host.Context())).Failure);
    }

    [Fact]
    public async Task Host_capabilities_and_named_policy_selection_fail_closed()
    {
        await using var host = Create("jit");
        host.Provider.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities = ProviderCapabilities.ExternalOidcDefault;
        Assert.Equal(ExternalAdmissionFailure.OwnershipDenied, (await host.Service.AssessAsync(await host.Context())).Failure);
        await using var unavailable = Create("not-installed");
        Assert.Equal(ExternalAdmissionFailure.PolicyUnavailable, (await unavailable.Service.AssessAsync(await unavailable.Context())).Failure);
        await using var ambiguous = Create("jit", custom: new CustomPolicy("jit", []));
        Assert.Equal(ExternalAdmissionFailure.PolicyUnavailable, (await ambiguous.Service.AssessAsync(await ambiguous.Context())).Failure);
    }

    [Fact]
    public async Task Integrator_policy_can_select_declared_Elsa_roles_and_cannot_grant_outside_either_boundary()
    {
        await using var host = Create("integrator", ["reader", "operator"], custom: new CustomPolicy("integrator", ["reader"]));
        await host.Role("reader", "workflows:read");
        var result = await host.Service.AssessAsync(await host.Context());
        Assert.Equal(ExternalAdmissionKind.NewUser, result.Kind);
        Assert.Equal(["reader"], result.RoleIds);
        await using var escalation = Create("integrator", ["reader"], custom: new CustomPolicy("integrator", ["admin"]));
        await escalation.Role("admin", "*");
        Assert.Equal(ExternalAdmissionFailure.RoleBoundary, (await escalation.Service.AssessAsync(await escalation.Context())).Failure);
        await using var deployment = Create("jit", ["admin"]);
        deployment.Provider.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.AllowedRoleIds = ["reader"];
        Assert.Equal(ExternalAdmissionFailure.RoleBoundary, (await deployment.Service.AssessAsync(await deployment.Context())).Failure);
    }

    [Fact]
    public async Task Matching_cannot_attach_a_provider_to_a_more_privileged_existing_user()
    {
        await using var host = Create("match", ["reader"], "verified-email");
        await host.Role("reader", "workflows:read");
        await host.Role("admin", "*");
        await host.Users.SaveAsync(User("ada", ["admin"]));
        Assert.Equal(ExternalAdmissionFailure.RoleBoundary, (await host.Service.AssessAsync(await host.Context(verified: true))).Failure);
        await host.Users.SaveAsync(User("ada", ["reader"]) with { DirectPermissions = new HashSet<string> { "*" } });
        Assert.Equal(ExternalAdmissionFailure.RoleBoundary, (await host.Service.AssessAsync(await host.Context(verified: true))).Failure);
    }

    [Fact]
    public async Task Matching_checks_membership_again_in_the_atomic_link_operation()
    {
        await using var host = Create("match", ["reader"], "verified-email");
        await host.Role("reader", "workflows:read");
        await host.Users.SaveAsync(User("ada", ["reader"]));
        host.BeforeAdmit = async () => await ((ITenantMembershipStore)host.Store).SaveAsync(new("default", "ada", TenantMembershipStatus.Active,
            new HashSet<string> { "admin" }, new HashSet<string>()));
        var result = await host.Service.AdmitAsync(await host.Context(verified: true));
        Assert.Equal(ExternalAdmissionFailure.Conflict, result.Failure);
        Assert.Null(await host.Links.FindByKeyAsync((await host.Context()).Identity));
    }

    [Fact]
    public async Task Linked_user_status_membership_and_deleted_roles_are_checked_without_importing_claims()
    {
        await using var host = Create("reject");
        await host.Users.SaveAsync(User("ada", ["deleted"]));
        var context = await host.Context();
        await host.Links.SaveAsync(new("default", "company", "subject", "ada", DateTimeOffset.UnixEpoch, null, ExternalIdentityLinkPolicy.Admin) { Issuer = "https://issuer/" });
        Assert.Equal(ExternalAdmissionFailure.RoleUnavailable, (await host.Service.AssessAsync(context)).Failure);
        await host.Users.SaveAsync(User("ada", []));
        await ((ITenantMembershipStore)host.Store).SaveAsync(new("default", "ada", TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>()));
        Assert.Equal(ExternalAdmissionFailure.MembershipUnavailable, (await host.Service.AssessAsync(context)).Failure);
        await host.Users.SetStatusAsync("default", "ada", UserStatus.Disabled);
        Assert.Equal(ExternalAdmissionFailure.UserUnavailable, (await host.Service.AssessAsync(context)).Failure);
    }

    [Fact]
    public async Task Preview_can_assess_a_disabled_draft_but_normal_admission_rechecks_lifecycle_and_material()
    {
        await using var host = Create("jit");
        var context = await host.Context();
        var options = host.Provider.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        options.Connections[0] = options.Connections[0] with { Enabled = false };
        Assert.Equal(ExternalAdmissionKind.NewUser, (await host.Service.AssessAsync(await host.Context())).Kind);
        Assert.Equal(ExternalAdmissionFailure.ConnectionUnavailable, (await host.Service.AdmitAsync(context)).Failure);
        options.Connections[0] = options.Connections[0] with { Enabled = true, Settings = new Dictionary<string, string> { ["authority"] = "https://changed/" } };
        Assert.Equal(ExternalAdmissionFailure.StaleConnection, (await host.Service.AdmitAsync(context)).Failure);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task Proof_scope_conflicts_and_duplicate_identifiers_refuse_before_any_mutation()
    {
        await using var host = Create("jit");
        var context = await host.Context();
        Assert.Equal(ExternalAdmissionFailure.IdentityMismatch, (await host.Service.AssessAsync(context with { Identity = new("other", "company", "https://issuer/", "subject") })).Failure);
        Assert.Equal(ExternalAdmissionFailure.IdentityMismatch, (await host.Service.AssessAsync(context with { Claims = context.Claims.Concat(new[] { new Claim("sub", "different") }).ToArray() })).Failure);
        Assert.Equal(0, host.Writes);
    }

    [Fact]
    public async Task Assessment_uses_its_owned_tenant_scope_and_preserves_the_callers_scope()
    {
        await using var host = Create("jit");
        await using var caller = host.Provider.CreateAsyncScope();
        var binder = caller.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>();
        binder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope("unrelated")));
        var result = await caller.ServiceProvider.GetRequiredService<IExternalAdmissionService>().AssessAsync(await host.Context());
        Assert.Equal(ExternalAdmissionKind.NewUser, result.Kind);
        Assert.Equal(new PersistenceScope("unrelated"), caller.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current.Scope);
    }

    private static Host Create(string policy, string[]? roles = null, string? matcher = null, OwnershipMode mode = OwnershipMode.FoundationOwned, IExternalProvisioningPolicy? custom = null)
    {
        var store = new InMemoryIdentityStore();
        var tracker = new TrackingStore(store);
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<FoundationIdentityOptions>(options => options.OwnershipMode = mode);
        services.AddSingleton<IUserStore>(tracker);
        services.AddSingleton<IExternalIdentityStore>(tracker);
        services.AddSingleton<IRoleStore>(store);
        services.AddSingleton<ITenantMembershipStore>(store);
        if (custom is not null) services.AddSingleton(custom);
        services.AddFoundationExternalAuthentication(options => options.Connections.Add(new()
        {
            Key = "company", Enabled = true,
            Admission = new() { Type = policy, DefaultRoleIds = roles ?? [], Matcher = matcher }
        }));
        return new(services.BuildServiceProvider(), store, tracker);
    }

    private static UserRecord User(string id, string[] roles) => new(id, "default", id, "ada@example.test", "Ada", UserStatus.Active,
        ResourceOwnership.Foundation, roles.ToHashSet(StringComparer.Ordinal), new HashSet<string>());

    private sealed class CustomPolicy(string name, string[] roles) : IExternalProvisioningPolicy
    {
        public int Calls { get; private set; }
        public string Name => name;
        public ValueTask<ExternalProvisioningDecision> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new ExternalProvisioningDecision { Kind = ExternalAdmissionKind.NewUser, RoleIds = roles });
        }
    }

    private sealed class Host(ServiceProvider provider, InMemoryIdentityStore store, TrackingStore tracker) : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;
        public InMemoryIdentityStore Store => store;
        public IUserStore Users => tracker;
        public IExternalIdentityStore Links => tracker;
        public int Writes => tracker.Writes;
        public Func<Task>? BeforeAdmit { set => tracker.BeforeAdmit = value; }
        public IExternalAdmissionService Service => provider.GetRequiredService<IExternalAdmissionService>();
        public void Reset() => tracker.Writes = 0;
        public ValueTask Role(string id, string permission) => store.SaveAsync(new RoleRecord(id, "default", id, null, new HashSet<string> { permission }, false));
        public async Task<ExternalAdmissionContext> Context(bool verified = false)
        {
            var connection = Assert.Single((await provider.GetRequiredService<IConnectionRegistry>().ReadAsync()).Connections);
            var claims = new List<Claim> { new("iss", "https://issuer/"), new("sub", "subject"), new("email", "ada@example.test"), new("name", "Ada") };
            if (verified) claims.Add(new("email_verified", "true", ClaimValueTypes.Boolean));
            return new(connection, new("default", "company", "https://issuer/", "subject"), claims);
        }
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class TrackingStore(InMemoryIdentityStore inner) : IUserStore, IExternalIdentityStore
    {
        public bool SupportsQualifiedKeys { get; set; } = true;
        public bool SupportsAtomicAdmission { get; set; } = true;
        public int Writes;
        public Func<Task>? BeforeAdmit;
        public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default) => ((IUserStore)inner).FindAsync(tenantId, userId, cancellationToken);
        public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) => inner.FindByEmailAsync(tenantId, email, cancellationToken);
        public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default) { Writes++; return inner.SaveAsync(user, cancellationToken); }
        public ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default) { Writes++; return inner.SetStatusAsync(tenantId, userId, status, cancellationToken); }
        public ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default) { Writes++; return inner.DeleteAsync(tenantId, userId, cancellationToken); }
        public ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default) => inner.FindBySubjectAsync(tenantId, provider, providerSubject, cancellationToken);
        public ValueTask<ExternalIdentityRecord?> FindByKeyAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default) => inner.FindByKeyAsync(key, cancellationToken);
        public ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default) => inner.ListForUserAsync(tenantId, userId, cancellationToken);
        public ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default) { Writes++; return inner.SaveAsync(externalIdentity, cancellationToken); }
        public async ValueTask<ExternalUserAdmissionResult> AdmitAsync(ExternalUserAdmission request, CancellationToken cancellationToken = default)
        {
            if (BeforeAdmit is not null) await BeforeAdmit();
            Writes++;
            return await inner.AdmitAsync(request, cancellationToken);
        }
    }
}
