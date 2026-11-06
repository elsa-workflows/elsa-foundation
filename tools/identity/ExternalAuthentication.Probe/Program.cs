using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using MinimalEndpoints;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication.Api;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Oidc;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Tests;
using Elsa.Foundation.Identity.Tests.ManagedOidc;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

var ownedRoot = Option("--owned-root") ?? throw new InvalidOperationException("Supply a new owned --owned-root directory. The probe never uses existing host stores.");
ownedRoot = Path.GetFullPath(ownedRoot);
var marker = Path.Combine(ownedRoot, ".elsa-3579-owned-probe");
if (Directory.Exists(ownedRoot) && Directory.EnumerateFileSystemEntries(ownedRoot).Any() && !File.Exists(marker))
    throw new InvalidOperationException("Refusing to use a nonempty directory without this probe's ownership marker.");
if (File.Exists(marker) && await File.ReadAllTextAsync(marker) != "elsa-external-authentication-probe/v1")
    throw new InvalidOperationException("The supplied directory has an unsupported ownership marker.");
Directory.CreateDirectory(ownedRoot);
await File.WriteAllTextAsync(marker, "elsa-external-authentication-probe/v1");
var settingsPath = Path.Combine(ownedRoot, "probe-settings.json");
var settings = File.Exists(settingsPath)
    ? JsonSerializer.Deserialize<ProbeSettings>(await File.ReadAllTextAsync(settingsPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Invalid owned probe settings.")
    : new ProbeSettings();
var listenValue = Option("--listen") ?? settings.Listen;
if (listenValue is not null && (!Uri.TryCreate(listenValue, UriKind.Absolute, out var listen) || !listen.IsLoopback || listen.Scheme != "http" || listen.AbsolutePath != "/" || listen.UserInfo.Length != 0))
    throw new InvalidOperationException("The owned probe listen address must be loopback HTTP.");
if (settings.PublicBaseUrl is not null && (!Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var publicBase) || !publicBase.IsLoopback || publicBase.Scheme != "http" || publicBase.AbsolutePath != "/backend" || publicBase.UserInfo.Length != 0 || publicBase.Query.Length != 0 || publicBase.Fragment.Length != 0))
    throw new InvalidOperationException("The owned probe public base must be loopback HTTP at /backend.");
string OwnedFile(string relative)
{
    var path = Path.GetFullPath(relative, ownedRoot);
    if (!path.StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Probe input must remain inside its owned root.");
    return path;
}
var clientSecret = settings.ClientSecretFile is null ? ControlledOidcServer.ClientSecret : await File.ReadAllTextAsync(OwnedFile(settings.ClientSecretFile));
if (settings.StudioUrl is not null && (!Uri.TryCreate(settings.StudioUrl, UriKind.Absolute, out var studioOrigin) || !studioOrigin.IsLoopback || studioOrigin.Scheme != "http" || studioOrigin.AbsolutePath != "/" || studioOrigin.UserInfo.Length != 0))
    throw new InvalidOperationException("The owned Studio origin must be loopback HTTP.");
var uiValue = Option("--ui");
Uri? ui = null;
if (uiValue is not null && (!Uri.TryCreate(uiValue, UriKind.Absolute, out ui) || !ui.IsLoopback || ui.Scheme != "http" || ui.UserInfo.Length != 0))
    throw new InvalidOperationException("The optional UI must be an owned loopback HTTP development server.");
await using var company = await ControlledOidcServer.Start();
await using var partner = await ControlledOidcServer.Start();
if (args.Contains("--providers-only", StringComparer.Ordinal))
{
    var publicBackend = Option("--public-backend");
    if (!Uri.TryCreate(publicBackend, UriKind.Absolute, out var publicUri) || !publicUri.IsLoopback || publicUri.Scheme != "http" || publicUri.AbsolutePath != "/backend" || publicUri.UserInfo.Length != 0)
        throw new InvalidOperationException("The provider fixture requires an owned loopback /backend public URL.");
    foreach (var (key, provider) in new[] { ("company", company), ("partner", partner) })
        foreach (var logicalKey in new[] { key, "studio-" + key })
            foreach (var purpose in new[] { "signin", "preview" })
                provider.RegisteredCallbacks.Add($"{publicBackend}/identity/external/{purpose}/{logicalKey}/callback");
    var keeperBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
    keeperBuilder.Logging.ClearProviders(); keeperBuilder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    await using var keeper = keeperBuilder.Build();
    var controlKey = await File.ReadAllTextAsync(OwnedFile(Option("--control-key-file") ?? "control-key.txt"));
    keeper.MapPost("/__probe/subject", async (HttpContext context) =>
    {
        if (context.Request.Headers["X-Owned-Probe-Control"] != controlKey) return Results.Unauthorized();
        if (context.Request.ContentLength is null or > 4096) return Results.BadRequest();
        var change = await context.Request.ReadFromJsonAsync<ProbeSubjectChange>(context.RequestAborted);
        if (change is null || change.Provider is not ("company" or "partner") || string.IsNullOrEmpty(change.Subject) || change.Subject.Length > 400) return Results.BadRequest();
        (change.Provider == "company" ? company : partner).Subject = change.Subject;
        return Results.Ok();
    }).AllowPublic("owned-provider-fixture", "Loopback-only test controller requires this invocation's private control key.");
    await keeper.StartAsync();
    await File.WriteAllTextAsync(Path.Combine(ownedRoot, "providers.json"), JsonSerializer.Serialize(new { Instance = Option("--instance"),
        CompanyIssuer = company.Issuer, PartnerIssuer = partner.Issuer, ControlledOidcServer.ClientId, ControlUrl = keeper.Urls.Single() }));
    await keeper.WaitForShutdownAsync(); return;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls(listenValue ?? "http://127.0.0.1:0");
var services = builder.Services;
services.AddAuthorization();
services.AddElsaEndpoints();
services.AddApiCapabilities();
if (settings.StudioUrl is not null)
{
    services.AddCors(options => options.AddPolicy("owned-studio", policy => policy.WithOrigins(settings.StudioUrl).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    services.Configure<AspNetCoreIdentityOptions>(options => options.AllowedReturnUrlOrigins.Add(settings.StudioUrl));
}
services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ownedRoot, "keys"))).SetApplicationName("elsa-3579-owned-probe");
if (settings.DatabaseProvider is not ("Sqlite" or "PostgreSql")) throw new InvalidOperationException("The owned probe qualifies SQLite or PostgreSQL only.");
string PostgreSqlConnection(string? file)
{
    if (file is null) throw new InvalidOperationException("PostgreSQL needs an owned connection file.");
    var value = File.ReadAllText(OwnedFile(file)); var parsed = new NpgsqlConnectionStringBuilder(value);
    if (parsed.Host is not ("127.0.0.1" or "localhost" or "::1") || parsed.Port <= 1024 ||
        parsed.Database?.StartsWith("elsa3579_", StringComparison.Ordinal) != true || parsed.Username?.StartsWith("elsa3579_", StringComparison.Ordinal) != true)
        throw new InvalidOperationException("Refusing a PostgreSQL target outside the owned loopback fixture namespace.");
    return value;
}
var database = settings.DatabaseProvider == "Sqlite" ? $"Data Source={Path.Combine(ownedRoot, "identity.db")};Pooling=False" : PostgreSqlConnection(settings.ConnectionStringFile);
services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new() { Provider = settings.DatabaseProvider, ConnectionString = database }, TestAdmin.SeedOptions(), isDevelopmentOrDemo: true);
if (!Enum.TryParse<OwnershipMode>(settings.OwnershipMode, out var ownershipMode) || !Enum.IsDefined(ownershipMode)) throw new InvalidOperationException("Invalid owned fixture ownership mode.");
services.Configure<FoundationIdentityOptions>(options => options.OwnershipMode = ownershipMode);
if (settings.DatabaseManagement) services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = settings.DatabaseProvider, ConnectionString = database });
if (settings.SessionStore) services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = settings.DatabaseProvider, ConnectionString = database });
services.AddSingleton<ISecretValueResolver>(new ProbeSecrets(clientSecret, settings.SecretsAvailable));
services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
services.AddFoundationExternalAuthentication(options =>
{
    options.AllowDatabaseManagement = settings.DatabaseManagement; options.AllowDeploymentOverrides = settings.DatabaseManagement;
    if (settings.StudioUrl is not null) options.AllowedReturnUrlOrigins.Add(settings.StudioUrl);
    options.Connections = settings.EmptyCatalog ? [] : [Connection("company", "Company", settings.CompanyIssuer ?? company.Issuer, settings.ClientId, settings.Provisioning),
        Connection("partner", "Partner", settings.PartnerIssuer ?? partner.Issuer, settings.ClientId, settings.Provisioning)];
});
if (settings.ManagedApi) services.AddFoundationExternalAuthenticationApi();
services.AddFoundationIdentityApi();
services.AddOpenIddictVendorForTests(options =>
{
    if (settings.DatabaseProvider == "Sqlite") options.UseSqlite($"Data Source={Path.Combine(ownedRoot, "tokens.db")};Pooling=False");
    else options.UseNpgsql(PostgreSqlConnection(settings.TokenConnectionStringFile));
});
var signingPath = Path.Combine(ownedRoot, "signing.pk8");
if (!File.Exists(signingPath))
{
    using var key = RSA.Create(2048); await File.WriteAllBytesAsync(signingPath, key.ExportPkcs8PrivateKey());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(signingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
var signingKey = Convert.ToBase64String(await File.ReadAllBytesAsync(signingPath));
services.AddFoundationIdentityOpenIddict(options => { options.IsDevelopmentOrDemo = true; options.SigningKey = signingKey; });
services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddictIdentityDefaults.SelectorScheme;
    options.DefaultAuthenticateScheme = OpenIddictIdentityDefaults.SelectorScheme;
    options.DefaultChallengeScheme = OpenIddictIdentityDefaults.SelectorScheme;
});
if (settings.UiRecovery)
{
    var original = services.Single(descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionStore));
    if (original.Lifetime != ServiceLifetime.Scoped || original.ImplementationType is null)
        throw new InvalidOperationException("The owned fault fixture requires the ordinary scoped session implementation.");
    var implementation = original.ImplementationType;
    services.Remove(original);
    services.AddSingleton<ProbeSessionFaults>();
    services.AddScoped<IAuthenticationSessionStore>(provider => new ProbeSessionStore(
        (IAuthenticationSessionStore)ActivatorUtilities.CreateInstance(provider, implementation),
        provider.GetRequiredService<ProbeSessionFaults>()));
    services.EnsureReplacementContract<IAuthenticationSessionStore, ProbeSessionStore>();
}
await using var app = builder.Build();
app.UsePathBase("/backend"); app.UseRouting();
if (settings.StudioUrl is not null) app.UseCors("owned-studio");
app.UseAuthentication(); app.UseAuthorization();
ApiCapabilitiesApi.MapApiCapabilitiesApi(app);
FoundationIdentityApi.MapFoundationIdentityApi(app); AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(app);
if (settings.ManagedApi) ExternalAuthenticationApi.MapExternalAuthenticationApi(app);
app.MapGet("/resource", () => Results.Ok()).RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersRead);
object? metadata = null;
app.MapGet("/__probe", () => Results.Json(metadata)).AllowPublic("qualification-probe", "Owned development probe exposes only public route/provider metadata.");
app.MapGet("/__probe/state", async (HttpContext context) =>
{
    await using var scope = app.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
    var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
    var tokens = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
    var sessions = scope.ServiceProvider.GetService<ExternalAuthenticationDbContext>();
    long? sessionCount = null;
    if (sessions is not null)
    {
        await sessions.Database.OpenConnectionAsync(context.RequestAborted);
        await using var count = sessions.Database.GetDbConnection().CreateCommand();
        var table = sessions.Model.GetEntityTypes().Single(type => type.GetTableName() == "identity_authentication_sessions");
        count.CommandText = "SELECT COUNT(*) FROM " + sessions.GetService<ISqlGenerationHelper>().DelimitIdentifier(table.GetTableName()!, table.GetSchema());
        sessionCount = Convert.ToInt64(await count.ExecuteScalarAsync(context.RequestAborted));
    }
    return Results.Json(new { Users = await iam.Users.CountAsync(context.RequestAborted), Links = await iam.ExternalIdentities.CountAsync(context.RequestAborted),
        Tokens = await tokens.Set<global::OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().CountAsync(context.RequestAborted),
        Sessions = sessionCount, company.TokenRequests });
}).RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
if (settings.UiRecovery)
{
    var controlKey = await File.ReadAllTextAsync(OwnedFile("ui-control-key.txt"));
    app.MapPost("/__probe/ui-control", async (HttpContext context) =>
    {
        if (context.Request.Headers["X-Owned-Probe-Control"] != controlKey) return Results.Unauthorized();
        if (context.Request.ContentLength is null or > 4096) return Results.BadRequest();
        var command = await context.Request.ReadFromJsonAsync<ProbeUiControl>(context.RequestAborted);
        if (command?.Mode == "deny-preview") { company.DenyNextAuthorization(); return Results.Ok(new { armed = true }); }
        if (command?.Mode is not ("before-write" or "after-commit") || command.UserId is null || command.SessionId is null)
            return Results.BadRequest();
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
        var session = await store.FindAsync("default", command.SessionId, context.RequestAborted);
        if (session is null || session.UserId != command.UserId || session.RevokedAt is not null) return Results.BadRequest();
        return context.RequestServices.GetRequiredService<ProbeSessionFaults>().Arm(command.SessionId, command.Mode)
            ? Results.Ok(new { armed = true }) : Results.Conflict();
    }).RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
}
if (ui is not null)
{
    using var uiClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    app.MapFallback("/{**path}", async context =>
    {
        if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 404; return; }
        // Proxy owned development assets only. Never forward authentication/cookie headers to Vite.
        var target = new Uri(ui, context.Request.Path.Value!.TrimStart('/') + context.Request.QueryString);
        if (target.Scheme != ui.Scheme || target.Host != ui.Host || target.Port != ui.Port)
        { context.Response.StatusCode = 404; return; }
        using var response = await uiClient.GetAsync(target, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    });
    await Initialize(); await Run();
}
else { await Initialize(); await Run(); }

async Task Initialize()
{
    await using var scope = app.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
    await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.MigrateAsync();
    if (settings.DatabaseManagement) await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.MigrateAsync();
    if (settings.SessionStore) await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync();
}
async Task Run()
{
    await app.StartAsync();
    var backend = settings.PublicBaseUrl ?? app.Urls.Single().TrimEnd('/') + "/backend";
    app.Services.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.PublicBaseUrl = backend;
    await using (var scope = app.Services.CreateAsyncScope())
    {
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        if (settings.UiRecovery || settings.EmptyCatalog)
            await scope.ServiceProvider.GetRequiredService<IUserStore>().SaveAsync(new("pending-fixture-user", "default", "pending-fixture-user",
                null, "Pending fixture user", UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>()));
        var readerPermissions = new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead };
        if (settings.StudioUrl is not null) readerPermissions.Add("api-capabilities:read");
        await scope.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "default", "Reader", null, readerPermissions, false));
        if (settings.AdministratorPermissions is not null)
        {
            var admin = (await scope.ServiceProvider.GetRequiredService<IUserStore>().FindByEmailAsync("default", TestAdmin.Email))!;
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            foreach (var role in admin.RoleIds)
                await roles.SaveAsync((await roles.FindAsync("default", role))! with { Permissions = settings.AdministratorPermissions.ToHashSet(StringComparer.Ordinal) });
        }
        var flow = scope.ServiceProvider.GetRequiredService<IExternalAuthenticationFlowService>();
        foreach (var (key, provider) in new[] { ("company", company), ("partner", partner) })
        {
            foreach (var logicalKey in new[] { key, "studio-" + key })
            {
                provider.RegisteredCallbacks.Add(flow.GetCallbackUri(logicalKey, AuthenticationArtifactPurpose.SignIn)!);
                provider.RegisteredCallbacks.Add(flow.GetCallbackUri(logicalKey, AuthenticationArtifactPurpose.Preview)!);
            }
        }
    }
    metadata = new { Instance = Option("--instance"), Backend = backend, CompanyIssuer = settings.CompanyIssuer ?? company.Issuer,
        PartnerIssuer = settings.PartnerIssuer ?? partner.Issuer, settings.ClientId,
        CompanySecret = "client-company", PartnerSecret = "client-partner", Ui = ui?.AbsoluteUri };
    var json = JsonSerializer.Serialize(metadata);
    await File.WriteAllTextAsync(Path.Combine(ownedRoot, "probe.json"), json);
    Console.WriteLine(json);
    await app.WaitForShutdownAsync();
}
string? Option(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
static ConnectionRecord Connection(string key, string displayName, string issuer, string clientId, bool provisioning) => new()
{
    Key = key, DisplayName = displayName, Enabled = true, Preferred = key == "company", Settings = new Dictionary<string, string> { ["authority"] = issuer, ["clientId"] = clientId },
    Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("client-" + key, "text", "identity/external") },
    Admission = new() { Type = provisioning ? "jit" : "reject", DefaultRoleIds = provisioning ? ["reader"] : [] }
};
internal sealed class ProbeSecrets(string clientSecret, bool available) : ISecretValueResolver
{
    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(available && (reference.Name is "client-company" or "client-partner") && (reference.TypeName is null or "text") && (reference.Scope is null or "identity/external")
            ? ResolvedSecret.Success(clientSecret, new() { Id = reference.Name, TenantId = tenantId, Name = reference.Name, Scope = reference.Scope, Status = SecretStatus.Active, CurrentVersion = 1 })
            : ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "Unknown owned probe reference."));
}
internal sealed class ProbeSettings
{
    public string? Listen { get; init; }
    public string? CompanyIssuer { get; init; }
    public string? PartnerIssuer { get; init; }
    public string ClientId { get; init; } = ControlledOidcServer.ClientId;
    public string? ClientSecretFile { get; init; }
    public string? StudioUrl { get; init; }
    public string DatabaseProvider { get; init; } = "Sqlite";
    public string? ConnectionStringFile { get; init; }
    public string? TokenConnectionStringFile { get; init; }
    public string? PublicBaseUrl { get; init; }
    public bool DatabaseManagement { get; init; } = true;
    public bool Provisioning { get; init; }
    public string OwnershipMode { get; init; } = "FoundationOwned";
    public string[]? AdministratorPermissions { get; init; }
    public bool SessionStore { get; init; } = true;
    public bool ManagedApi { get; init; } = true;
    public bool SecretsAvailable { get; init; } = true;
    public bool UiRecovery { get; init; }
    public bool EmptyCatalog { get; init; }
}
internal sealed record ProbeSubjectChange(string Provider, string Subject);

internal sealed record ProbeUiControl(string Mode, string? UserId, string? SessionId);
