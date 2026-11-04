using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Api.AspNetCore;
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
var uiValue = Option("--ui");
Uri? ui = null;
if (uiValue is not null && (!Uri.TryCreate(uiValue, UriKind.Absolute, out ui) || !ui.IsLoopback || ui.Scheme != "http" || ui.UserInfo.Length != 0))
    throw new InvalidOperationException("The optional UI must be an owned loopback HTTP development server.");
await using var company = await ControlledOidcServer.Start();
await using var partner = await ControlledOidcServer.Start();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
var services = builder.Services;
services.AddAuthorization();
services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ownedRoot, "keys"))).SetApplicationName("elsa-3579-owned-probe");
var database = $"Data Source={Path.Combine(ownedRoot, "identity.db")};Pooling=False";
services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database }, TestAdmin.SeedOptions(), isDevelopmentOrDemo: true);
services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database });
services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = database });
services.AddSingleton<ISecretValueResolver, ProbeSecrets>();
services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
services.AddFoundationExternalAuthentication(options =>
{
    options.AllowDatabaseManagement = true; options.AllowDeploymentOverrides = true;
    options.Connections = [Connection("company", "Company", company), Connection("partner", "Partner", partner)];
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
app.UsePathBase("/backend"); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
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
        await scope.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "default", "Reader", null, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }, false));
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
    metadata = new { Instance = Option("--instance"), Backend = backend, CompanyIssuer = company.Issuer, PartnerIssuer = partner.Issuer, ControlledOidcServer.ClientId,
        CompanySecret = "client-company", PartnerSecret = "client-partner", Ui = ui?.AbsoluteUri };
    var json = JsonSerializer.Serialize(metadata);
    await File.WriteAllTextAsync(Path.Combine(ownedRoot, "probe.json"), json);
    Console.WriteLine(json);
    await app.WaitForShutdownAsync();
}
string? Option(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
static ConnectionRecord Connection(string key, string displayName, ControlledOidcServer provider) => new()
{
    Key = key, DisplayName = displayName, Enabled = true, Settings = new Dictionary<string, string> { ["authority"] = provider.Issuer, ["clientId"] = ControlledOidcServer.ClientId },
    Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("client-" + key, "text", "identity/external") }, Admission = new() { Type = "reject" }
};
internal sealed class ProbeSecrets : ISecretValueResolver
{
    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(reference.Name is "client-company" or "client-partner" && reference.TypeName is null or "text" && reference.Scope is null or "identity/external"
            ? ResolvedSecret.Success(ControlledOidcServer.ClientSecret, new() { Id = reference.Name, TenantId = tenantId, Name = reference.Name, Scope = reference.Scope, Status = SecretStatus.Active, CurrentVersion = 1 })
            : ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "Unknown owned probe reference."));
}
