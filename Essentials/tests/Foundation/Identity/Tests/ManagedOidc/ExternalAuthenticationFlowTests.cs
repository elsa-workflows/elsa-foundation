using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Oidc;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed class ExternalAuthenticationFlowTests
{
    [Theory]
    [InlineData("missing", "Development")]
    [InlineData("volatile", "Production")]
    public async Task Missing_authority_or_non_durable_production_authority_refuses_before_protocol_work(string authority, string environment)
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node(authority: authority, environment: environment);
        var result = await Flow(node).BeginAsync(new("company", AuthenticationArtifactPurpose.SignIn, Random()));
        Assert.Equal(ExternalFlowFailure.Unavailable, result.Failure);
        Assert.Null(result.AuthorizationUrl);
        Assert.Equal(0, harness.Server.TokenRequests);
        Assert.Equal(0, harness.Server.KeyRequests);
    }

    [Fact]
    public async Task Node_A_starts_real_OIDC_node_B_finishes_after_A_stops_and_restart_replay_does_not_exchange_again()
    {
        await using var harness = await Harness.Create();
        ExternalFlowCallback callback;
        await using (var nodeA = await harness.Node())
        {
            callback = await harness.Authorize(nodeA);
            await harness.AssertProtectedRows(nodeA, callback);
        }
        string user;
        await using (var nodeB = await harness.Node())
        {
            var completed = await Flow(nodeB).CompleteAsync(callback);
            Assert.Equal(ExternalFlowFailure.None, completed.Failure);
            user = Assert.IsType<string>(completed.UserId);
            Assert.Equal("/workflows?folder=one", completed.ReturnUrl);
            Assert.Equal(harness.Server.Issuer, completed.Provenance!.Issuer);
            Assert.Equal("opaque-Subject", completed.Provenance.Subject);
            Assert.Equal(1, harness.Server.TokenRequests);
            await using var scope = Scope(nodeB);
            var stored = await scope.ServiceProvider.GetRequiredService<IUserStore>().FindAsync("default", user);
            Assert.Equal(["reader"], stored!.RoleIds);
            Assert.Empty(stored.DirectPermissions);
        }
        await using var restarted = await harness.Node();
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(restarted).CompleteAsync(callback)).Failure);
        Assert.Equal(1, harness.Server.TokenRequests);
        await using var finalScope = Scope(restarted);
        Assert.Equal(1, await finalScope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Users.CountAsync());
        Assert.NotNull(await finalScope.ServiceProvider.GetRequiredService<IExternalIdentityStore>()
            .FindByKeyAsync(new("default", "company", harness.Server.Issuer, "opaque-Subject")));
    }

    [Fact]
    public async Task Wrong_browser_route_and_purpose_leave_the_real_flow_usable_and_two_callbacks_exchange_only_once()
    {
        await using var harness = await Harness.Create();
        await using var a = await harness.Node();
        await using var b = await harness.Node();
        var callback = await harness.Authorize(a);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(b).CompleteAsync(callback with { BrowserBinding = Random() })).Failure);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(b).CompleteAsync(callback with { ConnectionKey = "other" })).Failure);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(b).CompleteAsync(callback with { Purpose = AuthenticationArtifactPurpose.Preview }, Administrator())).Failure);
        Assert.Equal(0, harness.Server.TokenRequests);
        var completed = await Task.WhenAll(Flow(a).CompleteAsync(callback).AsTask(), Flow(b).CompleteAsync(callback).AsTask());
        Assert.Single(completed, result => result.Failure == ExternalFlowFailure.None);
        Assert.Single(completed, result => result.Failure == ExternalFlowFailure.ExpiredOrUsed);
        Assert.Equal(1, harness.Server.TokenRequests);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("material")]
    [InlineData("source")]
    [InlineData("secret")]
    [InlineData("expired")]
    [InlineData("callback")]
    [InlineData("provider-error")]
    public async Task Invalidated_or_failed_flows_never_send_the_code_or_provision_a_user(string change)
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node();
        var callback = await harness.Authorize(node);
        var options = node.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        switch (change)
        {
            case "disabled": options.Connections[0] = options.Connections[0] with { Enabled = false }; break;
            case "material": options.Connections[0] = options.Connections[0] with { Admission = new() { Type = "reject" } }; break;
            case "source":
                var catalog = node.GetRequiredService<IConnectionCatalogStore>();
                var current = await catalog.ReadAsync();
                Assert.True((await catalog.SaveAsync(new([options.Connections[0] with { Id = "override", OverridesDeployment = true }], current.Revision))).Saved);
                break;
            case "secret": harness.Secret.Version++; break;
            case "expired": harness.Clock.Now = harness.Clock.Now.AddMinutes(11); break;
            case "callback": options.PublicBaseUrl = "https://different.example"; break;
            case "provider-error": callback = callback with { ProviderError = true }; break;
        }
        var completed = await Flow(node).CompleteAsync(callback);
        Assert.NotEqual(ExternalFlowFailure.None, completed.Failure);
        Assert.Null(completed.UserId);
        Assert.Equal(0, harness.Server.TokenRequests);
        await using var scope = Scope(node);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Users.CountAsync());
        // A failed, correctly bound callback is consumed too.
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(node).CompleteAsync(callback)).Failure);
    }

    [Theory]
    [InlineData("application")]
    [InlineData("keys")]
    public async Task Mismatched_Data_Protection_keys_or_application_fail_closed_even_with_the_same_shared_database(string mismatch)
    {
        await using var harness = await Harness.Create();
        await using var first = await harness.Node();
        var callback = await harness.Authorize(first);
        await using var wrongRing = await harness.Node(applicationName: mismatch == "application" ? "unrelated-host" : "owned-flow-fixture",
            keyDirectory: mismatch == "keys" ? "unshared-keys" : "keys");
        Assert.Equal(ExternalFlowFailure.InvalidState, (await Flow(wrongRing).CompleteAsync(callback)).Failure);
        Assert.Equal(0, harness.Server.TokenRequests);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(first).CompleteAsync(callback)).Failure);
    }

    [Fact]
    public async Task A_modified_protected_payload_is_not_redeemed()
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node();
        var callback = await harness.Authorize(node);
        await using (var scope = Scope(node))
            await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.ExecuteSqlRawAsync(
                "UPDATE identity_authentication_artifacts SET ProtectedPayload = 'corrupted' ");
        Assert.Equal(ExternalFlowFailure.InvalidState, (await Flow(node).CompleteAsync(callback)).Failure);
        Assert.Equal(0, harness.Server.TokenRequests);
    }

    [Fact]
    public async Task Preview_of_a_disabled_draft_is_bound_to_the_initiating_administrator_and_never_admits_the_user()
    {
        await using var harness = await Harness.Create();
        await using var a = await harness.Node();
        var options = a.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        var catalog = a.GetRequiredService<IConnectionCatalogStore>();
        var draft = options.Connections[0] with { Id = "draft", Enabled = false };
        Assert.True((await catalog.SaveAsync(new([draft]))).Saved);
        var actor = Administrator();
        var callback = await harness.Authorize(a, AuthenticationArtifactPurpose.Preview, actor, recordId: "draft");
        Assert.Equal(ExternalFlowFailure.Denied, (await Flow(a).CompleteAsync(callback)).Failure);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(a).CompleteAsync(callback, Administrator("another-admin"))).Failure);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(a).CompleteAsync(callback with { Purpose = AuthenticationArtifactPurpose.SignIn })).Failure);
        var completed = await Flow(a).CompleteAsync(callback, actor);
        Assert.Equal(ExternalFlowFailure.None, completed.Failure);
        Assert.Null(completed.UserId);
        Assert.Null(completed.Provenance);
        var handle = Assert.IsType<string>(completed.PreviewResultHandle);
        await harness.AssertProtectedRows(a, callback, handle, "ada@example.test", "opaque-Subject", "workflows:read");
        var snapshot = await catalog.ReadAsync();
        Assert.True((await catalog.SaveAsync(snapshot with { Records = [draft with { Admission = new() { Type = "reject" } }] })).Saved);
        await using var b = await harness.Node();
        var reader = Flow(b);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await reader.ReadPreviewAsync("company", handle, Random(), actor)).Failure);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await reader.ReadPreviewAsync("company", handle, callback.BrowserBinding, Administrator("another-admin"))).Failure);
        Assert.Equal(ExternalFlowFailure.Denied, (await reader.ReadPreviewAsync("company", handle, callback.BrowserBinding, Administrator(permission: false))).Failure);
        var result = await reader.ReadPreviewAsync("company", handle, callback.BrowserBinding, actor);
        Assert.Equal(ExternalFlowFailure.None, result.Failure);
        Assert.True(result.Preview!.Stale); // The draft changed after the observation.
        Assert.Equal(ExternalAdmissionKind.NewUser, result.Preview.Admission);
        Assert.Equal(["reader"], result.Preview.RoleIds);
        Assert.Equal(["workflows:read"], result.Preview.Permissions);
        Assert.Equal("Ada", result.Preview.DisplayName);
        Assert.Equal("ada@example.test", result.Preview.Email);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await reader.ReadPreviewAsync("company", handle, callback.BrowserBinding, actor)).Failure);
        Assert.Equal("admin", actor.FindFirstValue(IdentityClaimTypes.UserId));
        await using var scope = Scope(b);
        var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
        Assert.Equal(0, await iam.Users.CountAsync());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(new("default", "company", harness.Server.Issuer, "opaque-Subject")));
    }

    [Fact]
    public async Task Preview_result_expires_and_new_sign_in_ignores_external_roles_when_no_roles_are_configured()
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node();
        var options = node.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        options.Connections[0] = options.Connections[0] with { Admission = new() { Type = "jit" } };
        var preview = await harness.Authorize(node, AuthenticationArtifactPurpose.Preview, Administrator());
        var completed = await Flow(node).CompleteAsync(preview, Administrator());
        harness.Clock.Now = harness.Clock.Now.AddMinutes(6);
        Assert.Equal(ExternalFlowFailure.ExpiredOrUsed, (await Flow(node).ReadPreviewAsync("company", completed.PreviewResultHandle!, preview.BrowserBinding, Administrator())).Failure);
        var callback = await harness.Authorize(node);
        var signedIn = await Flow(node).CompleteAsync(callback);
        Assert.Equal(ExternalFlowFailure.None, signedIn.Failure);
        await using var scope = Scope(node);
        var user = await scope.ServiceProvider.GetRequiredService<IUserStore>().FindAsync("default", signedIn.UserId!);
        Assert.Empty(user!.RoleIds);
    }

    [Theory]
    [InlineData("//attacker.example")]
    [InlineData("/\\attacker.example")]
    [InlineData("https://attacker.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://elsa.example@attacker.example")]
    [InlineData("/workflows\r\nLocation: https://attacker.example")]
    public async Task Unapproved_return_destinations_do_not_start_a_flow(string returnUrl)
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node();
        var started = await Flow(node).BeginAsync(new("company", AuthenticationArtifactPurpose.SignIn, Random(), returnUrl));
        Assert.Equal(ExternalFlowFailure.InvalidReturnUrl, started.Failure);
        Assert.Null(started.AuthorizationUrl);
    }

    [Fact]
    public async Task Callback_uses_trusted_base_path_and_return_destinations_are_rechecked_on_callback()
    {
        await using var harness = await Harness.Create();
        await using var node = await harness.Node();
        var flow = Flow(node);
        Assert.Equal("https://elsa.example/backend/identity/external/signin/company/callback", flow.GetCallbackUri("company", AuthenticationArtifactPurpose.SignIn));
        var options = node.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;
        options.AllowedReturnUrlOrigins.Add("https://studio.example");
        var callback = await harness.Authorize(node, returnUrl: "https://studio.example/workflows");
        options.AllowedReturnUrlOrigins.Clear();
        Assert.Equal(ExternalFlowFailure.InvalidReturnUrl, (await flow.CompleteAsync(callback)).Failure);
        Assert.Equal(0, harness.Server.TokenRequests);
        Assert.Equal(ExternalFlowFailure.Denied, (await flow.BeginAsync(new("company", AuthenticationArtifactPurpose.Preview, Random(), ConnectionRecordId: "owned")) ).Failure);
    }

    private static IExternalAuthenticationFlowService Flow(IServiceProvider node) => node.GetRequiredService<IExternalAuthenticationFlowService>();
    private static string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static ClaimsPrincipal Administrator(string id = "admin", bool permission = true) => new(new ClaimsIdentity(
        new[] { new Claim(IdentityClaimTypes.Normalized, "v1"), new Claim(IdentityClaimTypes.UserId, id), new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(IdentityClaimTypes.TenantId, "default"), new Claim(IdentityClaimTypes.Permission, permission ? DefaultIdentityPermissionKeys.IdentityProvidersManage : "unrelated") }, "flow-tests"));
    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        return scope;
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"elsa-3579-broker-{Guid.NewGuid():n}");
        public ControlledOidcServer Server { get; private set; } = null!;
        public SecretResolver Secret { get; } = new();
        public MutableClock Clock { get; } = new();
        public static async Task<Harness> Create()
        {
            var harness = new Harness { Server = await ControlledOidcServer.Start() };
            Directory.CreateDirectory(harness.directory);
            return harness;
        }
        public async Task<ServiceProvider> Node(string applicationName = "owned-flow-fixture", string keyDirectory = "keys",
            string authority = "durable", string environment = "Development")
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ISecretValueResolver>(Secret);
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, keyDirectory))).SetApplicationName(applicationName);
            var connectionString = $"Data Source={Path.Combine(directory, "auth.db")};Pooling=False";
            services.AddIdentityIamEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
            services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
            if (authority == "durable") services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
            if (authority == "volatile") services.AddScoped<IAuthenticationArtifactStore, NonDurableAuthority>();
            services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
            services.AddFoundationExternalAuthentication(options =>
            {
                options.PublicBaseUrl = "https://elsa.example/backend";
                options.AllowDatabaseManagement = true;
                options.AllowDeploymentOverrides = true;
                options.Connections.Add(new()
                {
                    Id = "owned", Key = "company", Enabled = true, Admission = new() { Type = "jit", DefaultRoleIds = ["reader"] },
                    Settings = new Dictionary<string, string> { ["authority"] = Server.Issuer, ["clientId"] = ControlledOidcServer.ClientId },
                    Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("client", SecretTypeNames.Text, "identity/external") }
                });
            });
            services.AddNormalizedAuthenticationType("flow-tests");
            var node = services.BuildServiceProvider();
            await using var scope = Scope(node);
            await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.MigrateAsync();
            if (authority == "durable") await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            if (await roles.FindAsync("default", "reader") is null)
                await roles.SaveAsync(new("reader", "default", "Reader", null, new HashSet<string> { "workflows:read" }, false));
            Server.RegisteredCallbacks.Add(Flow(node).GetCallbackUri("company", AuthenticationArtifactPurpose.SignIn)!);
            Server.RegisteredCallbacks.Add(Flow(node).GetCallbackUri("company", AuthenticationArtifactPurpose.Preview)!);
            return node;
        }
        public async Task<ExternalFlowCallback> Authorize(ServiceProvider node, AuthenticationArtifactPurpose purpose = AuthenticationArtifactPurpose.SignIn,
            ClaimsPrincipal? actor = null, string returnUrl = "/workflows?folder=one", string recordId = "deployment:company")
        {
            var browserBinding = Random();
            var started = await Flow(node).BeginAsync(new("company", purpose, browserBinding, returnUrl, purpose == AuthenticationArtifactPurpose.Preview ? recordId : null), actor);
            Assert.Equal(ExternalFlowFailure.None, started.Failure);
            using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var response = await browser.GetAsync(started.AuthorizationUrl);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            return new("company", purpose, query["state"]!, browserBinding, query["code"], query["iss"]);
        }
        public async Task AssertProtectedRows(ServiceProvider node, ExternalFlowCallback callback, params string[] additionalSensitive)
        {
            await using var scope = Scope(node);
            var context = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT Id, BrowserBinding, AdministratorBinding, ProtectedPayload FROM identity_authentication_artifacts";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var row = string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue));
            foreach (var sensitive in new[] { callback.State, callback.BrowserBinding, callback.Code!, "CodeVerifier", "Nonce", "CredentialBinding", ControlledOidcServer.ClientSecret }.Concat(additionalSensitive))
                Assert.DoesNotContain(sensitive, row, StringComparison.Ordinal);
            Assert.False(await reader.ReadAsync());
        }
        public async ValueTask DisposeAsync() { await Server.DisposeAsync(); Directory.Delete(directory, true); }
    }
    private sealed class SecretResolver : ISecretValueResolver
    {
        public int Version = 1;
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResolvedSecret.Success(ControlledOidcServer.ClientSecret, new()
                { Id = "owned-secret", TenantId = tenantId, Name = reference.Name, Scope = reference.Scope, Status = SecretStatus.Active, CurrentVersion = Version }));
    }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class NonDurableAuthority : IAuthenticationArtifactStore
    {
        public bool IsDurable => false;
        public ValueTask<bool> TryCreateAsync(AuthenticationArtifact artifact, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Production must refuse this authority before any write.");
        public ValueTask<AuthenticationArtifact?> TryConsumeAsync(string tenantId, string id, AuthenticationArtifactPurpose purpose,
            string browserBinding, string? administratorBinding, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Production must refuse this authority before consumption.");
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OwnedFlowFixture";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
