using System.Security.Claims;
using System.Reflection;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using FastEndpoints;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

public sealed class AspNetCoreIdentityHighestSeamTests
{
    private const string Password = "Correct1!";
    private const string SignInRoute = "/test/sign-in";

    [Fact]
    public async Task Username_and_email_sign_in_issue_cookie_session_and_permission_claims_from_groundwork_authority()
    {
        await using var host = HighestSeamHost.Create();
        await host.SeedUserAsync(Password);

        var byName = await host.SignIn.PasswordSignInAsync("ada", Password, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, CancellationToken.None);
        var byEmail = await host.SignIn.PasswordSignInAsync("ada@example.test", Password, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, CancellationToken.None);

        Assert.True(byName.Succeeded);
        Assert.True(byEmail.Succeeded);
        Assert.NotNull(byName.Session);
        Assert.NotNull(byEmail.Session);
        Assert.Equal("authenticated", byName.Session.Status);
        Assert.Equal("authenticated", byEmail.Session.Status);
        Assert.Equal(AspNetCoreIdentityScenarioData.Ids.PrimaryUser, byName.Session.Subject);
        Assert.Contains("identity.users.read", byName.Session.Permissions);
        Assert.Contains("*", byName.Session.Permissions);
        Assert.Contains(host.Context.Response.Headers.SetCookie, value => value?.Contains(AspNetCoreIdentityDefaults.CookieScheme, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Bad_password_and_unknown_user_return_the_same_public_failure()
    {
        await using var host = HighestSeamHost.Create();
        await host.SeedUserAsync(Password);

        var badPassword = await host.SignIn.PasswordSignInAsync("ada", "wrong-password", AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, CancellationToken.None);
        var unknownUser = await host.SignIn.PasswordSignInAsync("missing-user", "wrong-password", AspNetCoreIdentityScenarioData.Ids.PrimaryTenant, CancellationToken.None);

        Assert.False(badPassword.Succeeded);
        Assert.False(unknownUser.Succeeded);
        Assert.Null(badPassword.Session);
        Assert.Null(unknownUser.Session);
    }

    [Fact]
    public async Task Cookie_principal_authorizes_a_permission_protected_endpoint()
    {
        await using var host = await HighestSeamWebHost.StartAsync();
        await host.SeedUserAsync(Password);
        var client = host.CreateClient();

        var signIn = await client.PostAsync(
            $"{SignInRoute}?username=ada&password={Uri.EscapeDataString(Password)}&tenantId={AspNetCoreIdentityScenarioData.Ids.PrimaryTenant}",
            content: null);

        Assert.Equal(StatusCodes.Status204NoContent, (int)signIn.StatusCode);
        var identityCookie = ExtractIdentityCookie(signIn);
        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/" + SecuredPingEndpoint.Route);
        protectedRequest.Headers.Add("Cookie", CookieHeader(identityCookie));

        var protectedResponse = await client.SendAsync(protectedRequest);

        Assert.True(
            protectedResponse.StatusCode == System.Net.HttpStatusCode.OK,
            $"Expected 200 but got {(int)protectedResponse.StatusCode}. Set-Cookie: {string.Join(" | ", signIn.Headers.TryGetValues("Set-Cookie", out var setCookie) ? setCookie : [])}. Location: {protectedResponse.Headers.Location}. WWW-Authenticate: {string.Join(" | ", protectedResponse.Headers.WwwAuthenticate.Select(value => value.ToString()))}.");
        Assert.Equal("\"pong\"", await protectedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_seeded_groundwork_host_signs_in_authorizes_survives_restart_and_locks_out()
    {
        var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
        await using (var firstHost = await ProductionSeededGroundworkWebHost.StartAsync(documents))
        {
            var firstClient = firstHost.CreateClient();
            var byName = await SignInAsync(firstClient, "admin", Password, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

            Assert.Equal(StatusCodes.Status204NoContent, (int)byName.StatusCode);
            var identityCookie = ExtractIdentityCookie(byName);
            Assert.Contains("secure", identityCookie, StringComparison.OrdinalIgnoreCase);

            using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/" + SecuredPingEndpoint.Route);
            protectedRequest.Headers.Add("Cookie", CookieHeader(identityCookie));
            var protectedResponse = await firstClient.SendAsync(protectedRequest);

            Assert.Equal(System.Net.HttpStatusCode.OK, protectedResponse.StatusCode);
            Assert.Equal("\"pong\"", await protectedResponse.Content.ReadAsStringAsync());
        }

        await using (var restartedHost = await ProductionSeededGroundworkWebHost.StartAsync(documents))
        {
            var restartedClient = restartedHost.CreateClient();
            var byEmail = await SignInAsync(restartedClient, "admin@example.test", Password, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
            var badPassword = await SignInAsync(restartedClient, "admin", "wrong-password", AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
            var unknownUser = await SignInAsync(restartedClient, "missing-admin", "wrong-password", AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

            Assert.Equal(StatusCodes.Status204NoContent, (int)byEmail.StatusCode);
            Assert.Equal(StatusCodes.Status401Unauthorized, (int)badPassword.StatusCode);
            Assert.Equal(StatusCodes.Status401Unauthorized, (int)unknownUser.StatusCode);

            using var claimsRequest = new HttpRequestMessage(HttpMethod.Get, "/" + SecuredClaimsEndpoint.Route);
            claimsRequest.Headers.Add("Cookie", CookieHeader(ExtractIdentityCookie(byEmail)));
            var claimsResponse = await restartedClient.SendAsync(claimsRequest);
            var claims = await claimsResponse.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.OK, claimsResponse.StatusCode);
            Assert.Contains("*", claims, StringComparison.Ordinal);
            Assert.Contains("identity.users.read", claims, StringComparison.Ordinal);

            for (var attempt = 0; attempt < 5; attempt++)
                Assert.Equal(
                    StatusCodes.Status401Unauthorized,
                    (int)(await SignInAsync(restartedClient, "admin", "wrong-password", AspNetCoreIdentityScenarioData.Ids.PrimaryTenant)).StatusCode);

            var lockedOut = await SignInAsync(restartedClient, "admin", Password, AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
            Assert.Equal(StatusCodes.Status401Unauthorized, (int)lockedOut.StatusCode);
        }
    }

    [Fact]
    public async Task Sign_in_binds_requested_tenant_before_manager_lookup()
    {
        await using var host = MultiTenantSignInHost.Create();
        await host.SeedUserAsync(AspNetCoreIdentityScenarioData.PrimaryUser, Password);
        await host.SeedUserAsync(CrossTenantUser(), "TenantBeta1!");

        var outcome = await host.PasswordSignInAsync("ada", "TenantBeta1!", AspNetCoreIdentityScenarioData.Ids.SecondaryTenant);

        Assert.True(outcome.Succeeded);
        Assert.Equal(AspNetCoreIdentityScenarioData.Ids.CrossTenantUser, outcome.Session!.Subject);
        Assert.Equal(AspNetCoreIdentityScenarioData.Ids.SecondaryTenant, outcome.Session.TenantId);
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task File_backed_sqlite_authority_survives_close_and_reopen()
    {
        await using var driver = new SqliteGroundworkProviderDriver();
        await driver.InitializeAsync();
        var manifest = IdentityStorageManifest.Create();
        var access = DocumentStoreAccess.Scoped(new StorageScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant));
        var sourceUser = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        var sourceRole = AspNetCoreIdentityScenarioData.CreateIdentityRole(AspNetCoreIdentityScenarioData.PrimaryRole);
        var login = new UserLoginInfo("github", "ada-external", "GitHub");
        var claim = new Claim(IdentityClaimTypes.Permission, "identity.users.read");

        await using (var firstClient = await driver.OpenClientAsync(manifest, access))
        {
            var stores = CreateSqliteStores(firstClient);
            Assert.True((await stores.Users.CreateAsync(sourceUser, CancellationToken.None)).Succeeded);
            Assert.True((await stores.Roles.CreateAsync(sourceRole, CancellationToken.None)).Succeeded);
            await stores.Users.AddToRoleAsync(sourceUser, sourceRole.NormalizedName!, CancellationToken.None);
            await stores.Users.AddClaimsAsync(sourceUser, [claim], CancellationToken.None);
            await stores.Users.AddLoginAsync(sourceUser, login, CancellationToken.None);
            await stores.Users.SetTokenAsync(sourceUser, "github", "refresh-token", "refresh-1", CancellationToken.None);
        }

        await using (var reopenedClient = await driver.OpenClientAsync(manifest, access))
        {
            var stores = CreateSqliteStores(reopenedClient);
            var reloadedByName = await stores.Users.FindByNameAsync(sourceUser.NormalizedUserName!, CancellationToken.None);
            var reloadedByLogin = await stores.Users.FindByLoginAsync(login.LoginProvider, login.ProviderKey, CancellationToken.None);

            Assert.NotNull(reloadedByName);
            Assert.Equal(sourceUser.Id, reloadedByName!.Id);
            Assert.Equal(sourceUser.Email, reloadedByName.Email);
            Assert.Equal(sourceUser.PasswordHash, reloadedByName.PasswordHash);
            Assert.NotNull(reloadedByLogin);
            Assert.Equal(sourceUser.Id, reloadedByLogin!.Id);
            Assert.Contains(await stores.Users.GetRolesAsync(reloadedByName, CancellationToken.None), value => value == sourceRole.Name);
            Assert.Contains(await stores.Users.GetClaimsAsync(reloadedByName, CancellationToken.None), value => value.Type == claim.Type && value.Value == claim.Value);
            Assert.Equal("refresh-1", await stores.Users.GetTokenAsync(reloadedByName, "github", "refresh-token", CancellationToken.None));
        }
    }

    private static string ExtractIdentityCookie(HttpResponseMessage response)
    {
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(AspNetCoreIdentityDefaults.CookieScheme, StringComparison.Ordinal))
            : null;
        Assert.False(string.IsNullOrWhiteSpace(cookie));
        return cookie!;
    }

    private static string CookieHeader(string setCookie) => setCookie.Split(';', 2)[0];

    private static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        string username,
        string password,
        string tenantId) =>
        await client.PostAsync(
            $"{SignInRoute}?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&tenantId={Uri.EscapeDataString(tenantId)}",
            content: null);

    private static AspNetCoreIdentityScenarioData.User CrossTenantUser() =>
        AspNetCoreIdentityScenarioData.Users.Single(user => user.Id == AspNetCoreIdentityScenarioData.Ids.CrossTenantUser);

    private static AspNetCoreIdentityScenarioData.Role CrossTenantRole() =>
        AspNetCoreIdentityScenarioData.Roles.Single(role => role.Id == AspNetCoreIdentityScenarioData.Ids.CrossTenantRole);

    private static AspNetCoreIdentityScenarioData.Membership CrossTenantMembership() =>
        AspNetCoreIdentityScenarioData.Memberships.Single(membership => membership.Id == AspNetCoreIdentityScenarioData.Ids.CrossTenantMembership);

    private static SqliteStores CreateSqliteStores(GroundworkProviderClient client)
    {
        var manifest = IdentityStorageManifest.Create();
        var bounded = client.DocumentStore as IBoundedDocumentStore
            ?? new TestBoundedDocumentStore(client.DocumentStore, manifest);
        var accessor = new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
            new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant)));
        return new SqliteStores(
            new GroundworkIdentityUserStore(client.DocumentStore, accessor, bounded),
            new GroundworkIdentityRoleStore(client.DocumentStore, accessor, bounded));
    }

    private sealed record SqliteStores(GroundworkIdentityUserStore Users, GroundworkIdentityRoleStore Roles);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class TestBoundedDocumentStore(IDocumentStore store, StorageManifest manifest)
        : IBoundedDocumentStore
    {
        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            var (indexIdentity, value) = Resolve(query);
#pragma warning disable GW0004
            var all = await store.QueryAsync(
                new DocumentStoreQuery(query.DocumentKind, indexIdentity, value),
                cancellationToken);
#pragma warning restore GW0004
            IEnumerable<DocumentEnvelope> window = all;
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), all.Count);
        }

        public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).TotalCount;

        public async Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).Documents.FirstOrDefault();

        public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            await FirstOrDefaultAsync(query, cancellationToken) is not null;

        private (string IndexIdentity, string Value) Resolve(DocumentQuery query)
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == query.DocumentKind);
            var indexIdentity = unit.PhysicalStorage?.BoundedQueries
                                    .SingleOrDefault(candidate => candidate.Identity == query.QueryIdentity)
                                    ?.IndexIdentity
                                ?? unit.Queries.Single(candidate => candidate.Identity == query.QueryIdentity).IndexIdentity;
            var index = unit.Indexes.Single(candidate => candidate.Identity == indexIdentity);
            var clause = query.Clauses.Count == 1
                ? query.Clauses[0]
                : throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have one clause.");
            var comparison = clause.Comparisons.Count == 1
                ? clause.Comparisons[0]
                : throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have one comparison.");
            var indexField = index.Fields.Count == 1
                ? index.Fields[0]
                : throw new InvalidOperationException($"Groundwork test index '{indexIdentity}' must have one field.");
            if (comparison.Operator != QueryComparisonOperator.Equal || comparison.Path != indexField.Path || comparison.Values.Count != 1)
                throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            var value = comparison.Values[0]
                        ?? throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have a non-null comparison value.");
            return (indexIdentity, value);
        }
    }

    private sealed class MultiTenantSignInHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly InMemoryDocumentStore _documents;

        private MultiTenantSignInHost(ServiceProvider provider, InMemoryDocumentStore documents)
        {
            _provider = provider;
            _documents = documents;
        }

        public static MultiTenantSignInHost Create()
        {
            var services = new ServiceCollection();
            var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());

            services.AddLogging();
            services.AddSingleton<IDocumentStore>(documents);
            services.AddSingleton<IBoundedDocumentStore>(documents);
            services.AddPersistenceCore(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.Configure<AspNetCoreIdentityOptions>(options =>
                options.DefaultTenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

            return new MultiTenantSignInHost(services.BuildServiceProvider(), documents);
        }

        public async Task SeedUserAsync(AspNetCoreIdentityScenarioData.User source, string password)
        {
            await using var scope = _provider.CreateAsyncScope();
            BindTenant(scope.ServiceProvider, source.TenantId);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(source);
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            user.TwoFactorEnabled = false;
            var result = await userManager.CreateAsync(user, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

            var elsaUsers = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var elsaRoles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            var role = string.Equals(source.TenantId, AspNetCoreIdentityScenarioData.Ids.SecondaryTenant, StringComparison.Ordinal)
                ? CrossTenantRole()
                : AspNetCoreIdentityScenarioData.PrimaryRole;
            var membership = string.Equals(source.TenantId, AspNetCoreIdentityScenarioData.Ids.SecondaryTenant, StringComparison.Ordinal)
                ? CrossTenantMembership()
                : AspNetCoreIdentityScenarioData.PrimaryMembership;

            await elsaUsers.SaveAsync(AspNetCoreIdentityScenarioData.CreateUserRecord(source), CancellationToken.None);
            await elsaRoles.SaveAsync(AspNetCoreIdentityScenarioData.CreateRoleRecord(role), CancellationToken.None);
            await memberships.SaveAsync(AspNetCoreIdentityScenarioData.CreateMembershipRecord(membership), CancellationToken.None);
        }

        public async Task<SignInOutcome> PasswordSignInAsync(string username, string password, string tenantId)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IIdentitySignInService>()
                .PasswordSignInAsync(username, password, tenantId, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            GC.KeepAlive(_documents);
        }

        private static void BindTenant(IServiceProvider serviceProvider, string tenantId) =>
            serviceProvider
                .GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
    }

    private sealed class HighestSeamHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly InMemoryDocumentStore _documents;

        private HighestSeamHost(ServiceProvider provider, InMemoryDocumentStore documents, DefaultHttpContext context)
        {
            _provider = provider;
            _documents = documents;
            Context = context;
        }

        public DefaultHttpContext Context { get; }

        public IIdentitySignInService SignIn => _provider.GetRequiredService<IIdentitySignInService>();

        public static HighestSeamHost Create()
        {
            var services = new ServiceCollection();
            var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
            var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

            services.AddLogging();
            services.AddSingleton<IDocumentStore>(documents);
            services.AddSingleton<IBoundedDocumentStore>(documents);
            services.AddScoped<IPersistenceAccessContextAccessor>(_ =>
                new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
                    new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant))));
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.Configure<AspNetCoreIdentityOptions>(options =>
                options.DefaultTenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);

            var provider = services.BuildServiceProvider();
            context.RequestServices = provider;
            return new HighestSeamHost(provider, documents, context);
        }

        public async Task SeedUserAsync(string password)
        {
            using var scope = _provider.CreateScope();
            var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            user.TwoFactorEnabled = false;
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var result = await userManager.CreateAsync(user, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

            var elsaUserStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var elsaRoleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            var membershipStore = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
            var userRecord = AspNetCoreIdentityScenarioData.CreateUserRecord(AspNetCoreIdentityScenarioData.PrimaryUser) with
            {
                DirectPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "identity.users.read" }
            };
            var roleRecord = AspNetCoreIdentityScenarioData.CreateRoleRecord(AspNetCoreIdentityScenarioData.PrimaryRole) with
            {
                Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }
            };
            var membership = AspNetCoreIdentityScenarioData.CreateMembershipRecord(AspNetCoreIdentityScenarioData.PrimaryMembership);

            await elsaUserStore.SaveAsync(userRecord, CancellationToken.None);
            await elsaRoleStore.SaveAsync(roleRecord, CancellationToken.None);
            await membershipStore.SaveAsync(membership, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            GC.KeepAlive(_documents);
        }

        private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
            : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } = current;
        }
    }

    private sealed class HighestSeamWebHost : IAsyncDisposable
    {
        private readonly IHost _host;

        private HighestSeamWebHost(IHost host)
        {
            _host = host;
        }

        public static async Task<HighestSeamWebHost> StartAsync()
        {
            var documents = new InMemoryDocumentStore(IdentityStorageManifest.Create());
            var host = await new HostBuilder()
                .UseEnvironment(Environments.Development)
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthorization();
                        services.AddSingleton<IDocumentStore>(documents);
                        services.AddSingleton<IBoundedDocumentStore>(documents);
                        services.AddScoped<IPersistenceAccessContextAccessor>(_ =>
                            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
                                new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant))));
                        services.AddFoundationAspNetCoreIdentityGroundwork();
                        services.Configure<AspNetCoreIdentityOptions>(options =>
                            options.DefaultTenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
                        services.AddFastEndpoints(options =>
                        {
                            options.Assemblies = [typeof(SecuredPingEndpoint).Assembly];
                            options.DisableAutoDiscovery = true;
                        });
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost(SignInRoute, async context =>
                            {
                                var username = context.Request.Query["username"].ToString();
                                var password = context.Request.Query["password"].ToString();
                                var tenantId = context.Request.Query["tenantId"].ToString();
                                var signIn = context.RequestServices.GetRequiredService<IIdentitySignInService>();
                                var outcome = await signIn.PasswordSignInAsync(username, password, tenantId, context.RequestAborted);
                                context.Response.StatusCode = outcome.Succeeded
                                    ? StatusCodes.Status204NoContent
                                    : StatusCodes.Status401Unauthorized;
                            });
                            endpoints.MapFastEndpoints(config =>
                            {
                                config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                                config.Security.RoleClaimType = IdentityClaimTypes.Role;
                            });
                        });
                    });
                })
                .StartAsync();

            return new HighestSeamWebHost(host);
        }

        public HttpClient CreateClient() => _host.GetTestClient();

        public async Task SeedUserAsync(string password)
        {
            await using var scope = _host.Services.CreateAsyncScope();
            await SeedHighestSeamUserAsync(scope.ServiceProvider, password);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
            : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } = current;
        }
    }

    public sealed class SecuredPingEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public const string Route = "test/secured-ping";

        public override void Configure()
        {
            Get(Route);
            ConfigurePermissions();
        }

        public override Task HandleAsync(CancellationToken ct) => Send.OkAsync("pong", ct);
    }

    public sealed class SecuredClaimsEndpoint : ElsaEndpointWithoutRequest<string>
    {
        public const string Route = "test/secured-claims";

        public override void Configure()
        {
            Get(Route);
            ConfigurePermissions();
        }

        public override Task HandleAsync(CancellationToken ct)
        {
            var permissions = User.FindAll(IdentityClaimTypes.Permission)
                .Select(claim => claim.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return Send.OkAsync(string.Join(",", permissions), ct);
        }
    }

    private sealed class ProductionSeededGroundworkWebHost : IAsyncDisposable
    {
        private const string CoreAssemblyName = "Elsa.Foundation.Identity.AspNetCoreIdentity";
        private const string SeedOptionsTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding.IdentitySeedOptions";
        private const string GroundworkRegistrationTypeName = "Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection.AspNetCoreIdentityGroundworkRegistration";
        private readonly IHost _host;

        private ProductionSeededGroundworkWebHost(IHost host)
        {
            _host = host;
        }

        public static async Task<ProductionSeededGroundworkWebHost> StartAsync(InMemoryDocumentStore documents)
        {
            var host = await new HostBuilder()
                .UseEnvironment(Environments.Production)
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthorization();
                        services.AddSingleton<IDocumentStore>(documents);
                        services.AddSingleton<IBoundedDocumentStore>(documents);
                        services.AddScoped<IPersistenceAccessContextAccessor>(_ =>
                            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(
                                new PersistenceScope(AspNetCoreIdentityScenarioData.Ids.PrimaryTenant))));

                        InvokeGroundworkRegistration(services, CreateSeedOptions());
                        services.Configure<AspNetCoreIdentityOptions>(options =>
                            options.DefaultTenantId = AspNetCoreIdentityScenarioData.Ids.PrimaryTenant);
                        services.AddFastEndpoints(options =>
                        {
                            options.Assemblies = [typeof(SecuredPingEndpoint).Assembly];
                            options.DisableAutoDiscovery = true;
                        });
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost(SignInRoute, async context =>
                            {
                                var username = context.Request.Query["username"].ToString();
                                var password = context.Request.Query["password"].ToString();
                                var tenantId = context.Request.Query["tenantId"].ToString();
                                var signIn = context.RequestServices.GetRequiredService<IIdentitySignInService>();
                                var outcome = await signIn.PasswordSignInAsync(username, password, tenantId, context.RequestAborted);
                                context.Response.StatusCode = outcome.Succeeded
                                    ? StatusCodes.Status204NoContent
                                    : StatusCodes.Status401Unauthorized;
                            });
                            endpoints.MapFastEndpoints(config =>
                            {
                                config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                                config.Security.RoleClaimType = IdentityClaimTypes.Role;
                            });
                        });
                    });
                })
                .StartAsync();

            return new ProductionSeededGroundworkWebHost(host);
        }

        public HttpClient CreateClient() => _host.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        private static object CreateSeedOptions()
        {
            var seedOptionsType = Type.GetType($"{SeedOptionsTypeName}, {CoreAssemblyName}", throwOnError: false);
            Assert.NotNull(seedOptionsType);
            var seedOptions = Activator.CreateInstance(seedOptionsType!);
            Assert.NotNull(seedOptions);
            Set(seedOptions!, "UserName", "admin");
            Set(seedOptions!, "Password", Password);
            Set(seedOptions!, "Email", "admin@example.test");
            Set(seedOptions!, "RoleName", "administrator");
            Set(seedOptions!, "IsDevelopmentSeed", false);
            return seedOptions!;
        }

        private static void InvokeGroundworkRegistration(IServiceCollection services, object seedOptions)
        {
            var registrationType = typeof(AspNetCoreIdentityGroundworkRegistration);
            Assert.Equal(GroundworkRegistrationTypeName, registrationType.FullName);
            var method = registrationType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(candidate =>
                {
                    if (candidate.Name != "AddFoundationAspNetCoreIdentityGroundwork")
                        return false;

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 2 &&
                           parameters[0].ParameterType == typeof(IServiceCollection) &&
                           parameters[1].ParameterType == seedOptions.GetType();
                });

            Assert.NotNull(method);
            method!.Invoke(null, [services, seedOptions]);
        }

        private static void Set(object target, string propertyName, object value)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property);
            property!.SetValue(target, value);
        }

        private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
            : IPersistenceAccessContextAccessor
        {
            public PersistenceAccessContext Current { get; } = current;
        }
    }

    private static async Task SeedHighestSeamUserAsync(IServiceProvider serviceProvider, string password)
    {
        var user = AspNetCoreIdentityScenarioData.CreateIdentityUser(AspNetCoreIdentityScenarioData.PrimaryUser);
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.TwoFactorEnabled = false;
        var userManager = serviceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

        var elsaUserStore = serviceProvider.GetRequiredService<IUserStore>();
        var elsaRoleStore = serviceProvider.GetRequiredService<IRoleStore>();
        var membershipStore = serviceProvider.GetRequiredService<ITenantMembershipStore>();
        var userRecord = AspNetCoreIdentityScenarioData.CreateUserRecord(AspNetCoreIdentityScenarioData.PrimaryUser) with
        {
            DirectPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "identity.users.read" }
        };
        var roleRecord = AspNetCoreIdentityScenarioData.CreateRoleRecord(AspNetCoreIdentityScenarioData.PrimaryRole) with
        {
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }
        };
        var membership = AspNetCoreIdentityScenarioData.CreateMembershipRecord(AspNetCoreIdentityScenarioData.PrimaryMembership);

        await elsaUserStore.SaveAsync(userRecord, CancellationToken.None);
        await elsaRoleStore.SaveAsync(roleRecord, CancellationToken.None);
        await membershipStore.SaveAsync(membership, CancellationToken.None);
    }
}
