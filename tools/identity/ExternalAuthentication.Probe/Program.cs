using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using NativeEndpoints;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication;
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
using Microsoft.Extensions.Options;

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
if (settings.Listen is not null && (!Uri.TryCreate(settings.Listen, UriKind.Absolute, out var listen) || !listen.IsLoopback || listen.Scheme != "http" || listen.AbsolutePath != "/" || listen.UserInfo.Length != 0))
    throw new InvalidOperationException("The owned probe listen address must be loopback HTTP.");
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
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls(settings.Listen ?? "http://127.0.0.1:0");
var services = builder.Services;
services.AddAuthorization();
services.AddApiCapabilities();
if (settings.StudioUrl is not null)
{
    services.AddCors(options => options.AddPolicy("owned-studio", policy => policy.WithOrigins(settings.StudioUrl).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    services.Configure<AspNetCoreIdentityOptions>(options => options.AllowedReturnUrlOrigins.Add(settings.StudioUrl));
}
services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ownedRoot, "keys"))).SetApplicationName("elsa-3579-owned-probe");
var database = $"Data Source={Path.Combine(ownedRoot, "identity.db")};Pooling=False";
services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database }, TestAdmin.SeedOptions(), isDevelopmentOrDemo: true);
services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database });
services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database });
services.AddSingleton<ISecretValueResolver>(new ProbeSecrets(clientSecret));
services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
services.AddFoundationExternalAuthentication(options =>
{
    options.AllowDatabaseManagement = true; options.AllowDeploymentOverrides = true;
    if (settings.StudioUrl is not null) options.AllowedReturnUrlOrigins.Add(settings.StudioUrl);
    options.Connections = [Connection("company", "Company", settings.CompanyIssuer ?? company.Issuer, settings.ClientId),
        Connection("partner", "Partner", settings.PartnerIssuer ?? partner.Issuer, settings.ClientId)];
});
services.AddFoundationExternalAuthenticationApi(); services.AddFoundationIdentityApi();
services.AddOpenIddictVendorForTests(options => options.UseSqlite($"Data Source={Path.Combine(ownedRoot, "tokens.db")};Pooling=False"));
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
await using var app = builder.Build();
app.UsePathBase("/backend"); app.UseRouting();
if (settings.StudioUrl is not null) app.UseCors("owned-studio");
app.UseAuthentication(); app.UseAuthorization();
ApiCapabilitiesApi.MapApiCapabilitiesApi(app);
FoundationIdentityApi.MapFoundationIdentityApi(app); AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(app); ExternalAuthenticationApi.MapExternalAuthenticationApi(app);
app.MapGet("/resource", () => Results.Ok()).RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersRead);
object? metadata = null;
app.MapGet("/__probe", () => Results.Json(metadata)).AllowPublic("qualification-probe", "Owned development probe exposes only public route/provider metadata.");
app.MapGet("/__probe/state", async (HttpContext context) =>
{
    await using var scope = app.Services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
    var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
    var tokens = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
    var sessions = scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>();
    await sessions.Database.OpenConnectionAsync(context.RequestAborted);
    await using var count = sessions.Database.GetDbConnection().CreateCommand();
    count.CommandText = "SELECT COUNT(*) FROM identity_authentication_sessions";
    var sessionCount = Convert.ToInt64(await count.ExecuteScalarAsync(context.RequestAborted));
    return Results.Json(new { Users = await iam.Users.CountAsync(context.RequestAborted), Links = await iam.ExternalIdentities.CountAsync(context.RequestAborted),
        Tokens = await tokens.Set<global::OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().CountAsync(context.RequestAborted),
        Sessions = sessionCount, company.TokenRequests });
}).RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
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
    await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync();
}
async Task Run()
{
    await app.StartAsync();
    var backend = app.Urls.Single().TrimEnd('/') + "/backend";
    app.Services.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.PublicBaseUrl = backend;
    await using (var scope = app.Services.CreateAsyncScope())
    {
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var readerPermissions = new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead };
        if (settings.StudioUrl is not null) readerPermissions.Add("api-capabilities:read");
        await scope.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "default", "Reader", null, readerPermissions, false));
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
static ConnectionRecord Connection(string key, string displayName, string issuer, string clientId) => new()
{
    Key = key, DisplayName = displayName, Enabled = true, Settings = new Dictionary<string, string> { ["authority"] = issuer, ["clientId"] = clientId },
    Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("client-" + key, "text", "identity/external") }, Admission = new() { Type = "reject" }
};
internal sealed class ProbeSecrets(string clientSecret) : ISecretValueResolver
{
    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(reference.Name is "client-company" or "client-partner" && reference.TypeName is null or "text" && reference.Scope is null or "identity/external"
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
}
