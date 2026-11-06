using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Acme.Processing;
using Acme.Processing.Core;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Studio.Dashboard.Api;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var profilesPath = builder.Configuration["profiles"] ?? throw new InvalidOperationException("Supply the owned private authentication profiles.");
var profiles = JsonSerializer.Deserialize<FixtureProfiles>(File.ReadAllText(profilesPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("Invalid owned authentication profiles.");
var enabled = builder.Configuration.GetValue("module-enabled", true);
var stateDirectory = builder.Configuration["state"] ?? throw new InvalidOperationException("Supply the owned state directory.");
Directory.CreateDirectory(stateDirectory);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddSingleton(profiles);
builder.Services.AddAuthentication("DashboardPackageProbe").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("DashboardPackageProbe", _ => { });
new FoundationIdentityAbstractionsFeature().ConfigureServices(builder.Services);
builder.Services.Configure<FoundationIdentityOptions>(options =>
{
    options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "DashboardPackageProbe" };
    options.PermissionPropagation = PermissionPropagationMode.TokenRefreshBoundary;
});
new EventsFeature().ConfigureServices(builder.Services);
var capabilities = new ApiCapabilitiesFeature(); capabilities.ConfigureServices(builder.Services);
var dashboard = new DashboardResourcesApiFeature(); dashboard.ConfigureServices(builder.Services);
builder.Services.AddDbContext<DashboardResourcesSqliteDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(stateDirectory, "dashboards.db")));
builder.Services.AddDashboardResourcesEntityFrameworkCore<DashboardResourcesSqliteDbContext>();
ProcessingFeature? module = enabled ? new ProcessingFeature() : null;
module?.ConfigureServices(builder.Services);
await using var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<DashboardResourcesSqliteDbContext>().Database.EnsureCreatedAsync();
app.UseAuthentication(); app.UseAuthorization();
capabilities.MapEndpoints(app, app.Environment);
dashboard.MapEndpoints(app, app.Environment);
module?.MapEndpoints(app, app.Environment);
app.MapGet("/__qualification/loaded", () =>
{
    using var scope = app.Services.CreateScope();
    return new {
    moduleEnabled = enabled,
    providerConfigured = false,
    contributionCount = scope.ServiceProvider.GetServices<IDashboardDatasetExtension>().Count(),
    assemblies = new[] { typeof(ProcessingFeature).Assembly, typeof(ProcessingRecord).Assembly, typeof(IDashboardQueryService).Assembly }
        .Select(assembly => new
        {
            name = assembly.GetName().Name, location = assembly.Location,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            moduleId = assembly.ManifestModule.ModuleVersionId,
            references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).Order(StringComparer.Ordinal).ToArray()
        }) };
}).RequireAuthorization();
await app.StartAsync();
Console.WriteLine("DASHBOARD_PACKAGE_PROBE_READY");
await app.WaitForShutdownAsync();

sealed record FixtureProfile(string Name, string Subject, string Tenant, string Token, IReadOnlyList<string> Permissions);
sealed record FixtureProfiles(IReadOnlyList<FixtureProfile> Profiles);

// Controlled local authentication provider, not an alternate permission evaluator or actor resolver.
// No production credential is read; the driver owns fresh random fixture tokens and omits them from receipts.
sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, FixtureProfiles profiles) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var entry = header.StartsWith("Bearer ", StringComparison.Ordinal) ? profiles.Profiles.SingleOrDefault(profile => profile.Token == header[7..]) : null;
        if (entry is null) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim>
        {
            new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.UserId, entry.Subject),
            new(IdentityClaimTypes.TenantId, entry.Tenant), new(ClaimTypes.NameIdentifier, entry.Subject),
            new(IdentityClaimTypes.Provider, "owned-package-fixture")
        };
        claims.AddRange(entry.Permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, "DashboardPackageProbe")), Scheme.Name)));
    }
}
