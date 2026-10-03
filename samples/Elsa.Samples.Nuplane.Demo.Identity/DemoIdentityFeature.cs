using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Demo.Identity;

/// <summary>
/// A self-contained identity composition for a feed-loaded Foundation Host demo. It owns the vendor-specific
/// OpenIddict EF store that Workbench normally registers at the process root, while keeping all Elsa feature
/// contracts and endpoint mapping inside the shell generation that loaded this package.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Foundation demo identity",
    Description = "Portable Foundation Identity, OpenIddict, login, token, and Studio CORS composition for a feed-loaded Foundation Host.")]
[UsesEfModule("Identity.Iam")]
[UsesEfModule("Identity.ProviderConfiguration")]
public sealed class DemoIdentityFeature : IWebShellFeature, IMiddlewareShellFeature
{
    public const string FeatureName = "FoundationDemoIdentity";
    public const string CorsPolicyName = "FoundationDemoIdentityStudio";

    [ManifestSetting(
        DisplayName = "Identity IAM connection string",
        Description = "SQLite database for users, roles, credentials, and Identity provider configuration.",
        Category = "Persistence",
        Secret = true)]
    public string IdentityConnectionString { get; set; } = "Data Source=foundation-host-demo-identity.db";

    [ManifestSetting(
        DisplayName = "OpenIddict connection string",
        Description = "SQLite database for OpenIddict applications, authorizations, scopes, and tokens.",
        Category = "Persistence",
        Secret = true)]
    public string OpenIddictConnectionString { get; set; } = "Data Source=foundation-host-demo-openiddict.db";

    [ManifestSetting(
        DisplayName = "Development or demo",
        Description = "Uses the ephemeral OpenIddict signing key and development cookie transport. Development environment is required.",
        Category = "Identity",
        DefaultValue = "true")]
    public bool IsDevelopmentOrDemo { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Identity issuer",
        Description = "Absolute issuer written to first-party access tokens. Set this to the Foundation Host origin.",
        Category = "Identity")]
    public string Issuer { get; set; } = "http://localhost:5311/";

    [ManifestSetting(DisplayName = "Seed admin username", Description = "Administrator username for the demo login page.", Category = "Identity")]
    public string? SeedAdminUserName { get; set; }

    [ManifestSetting(DisplayName = "Seed admin password", Description = "Administrator password for the demo login page.", Category = "Identity", Secret = true)]
    public string? SeedAdminPassword { get; set; }

    [ManifestSetting(DisplayName = "Seed admin email", Description = "Optional administrator email address.", Category = "Identity")]
    public string? SeedAdminEmail { get; set; }

    [ManifestSetting(DisplayName = "Seed admin role", Description = "Role granted to the seeded administrator.", Category = "Identity")]
    public string? SeedAdminRoleName { get; set; }

    [ManifestSetting(
        DisplayName = "Allowed Studio origins",
        Description = "Credentialed CORS and cross-origin login returnUrl origins. Include every Studio origin used by the demo.",
        Category = "Security")]
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:5313",
        "http://127.0.0.1:5313"
    ];

    public int Order => -100;

    public void ConfigureServices(IServiceCollection services)
    {
        var origins = NormalizeOrigins(AllowedOrigins);
        if (origins.Length == 0)
            throw new InvalidOperationException($"{FeatureName}:AllowedOrigins must contain at least one absolute origin.");

        services.AddElsaEndpoints();

        var seed = BuildInitialAdmin();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = IdentityConnectionString
            },
            seed,
            IsDevelopmentOrDemo,
            options =>
            {
                options.IsDefault = true;
                options.AllowedReturnUrlOrigins = origins;
            });

        services.AddIdentityProviderConfigurationEntityFrameworkCore(new IdentityProviderConfigurationEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = IdentityConnectionString
        }).AddEfModuleMigrations<IdentityProviderConfigurationDbContext>("Sqlite");

        services.AddFoundationIdentityOpenIddict(options =>
        {
            // Password login is the interactive default; OpenIddict validates the bearer token produced by
            // the identity API but should not become a second challenge provider in Studio bootstrap.
            options.IsDefault = false;
            options.IsDevelopmentOrDemo = IsDevelopmentOrDemo;
            options.ConnectionString = OpenIddictConnectionString;
            options.Issuer = Issuer;
        });

        services.AddDbContext<DemoOpenIddictDbContext>((_, builder) =>
            builder.UseSqlite(OpenIddictConnectionString));
        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<DemoOpenIddictDbContext>());

        services.AddSingleton<DemoOpenIddictStoreInitializer>();
        services.AddSingleton<IShellInitializer>(provider =>
            provider.GetRequiredService<DemoOpenIddictStoreInitializer>());

        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("Content-Disposition")));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        FoundationIdentityApi.MapFoundationIdentityApi(endpoints);
        AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(endpoints);
    }

    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment)
    {
        // CORS must run before authentication so Studio preflight and credentialed requests receive the
        // policy headers even when an API endpoint would otherwise challenge the request.
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
    }

    private IdentitySeedOptions? BuildInitialAdmin()
    {
        var hasUserName = !string.IsNullOrWhiteSpace(SeedAdminUserName);
        var hasPassword = !string.IsNullOrWhiteSpace(SeedAdminPassword);
        if (!hasUserName && !hasPassword)
            return null;
        if (!hasUserName || !hasPassword)
            throw new InvalidOperationException(
                $"{FeatureName}:SeedAdminUserName and SeedAdminPassword must be supplied together.");

        return new IdentitySeedOptions
        {
            UserName = SeedAdminUserName!,
            Password = SeedAdminPassword!,
            Email = string.IsNullOrWhiteSpace(SeedAdminEmail)
                ? $"{SeedAdminUserName}@elsa.local"
                : SeedAdminEmail!,
            RoleName = string.IsNullOrWhiteSpace(SeedAdminRoleName)
                ? IdentitySeedOptions.DefaultRoleName
                : SeedAdminRoleName!,
            IsDevelopmentSeed = IsDevelopmentOrDemo
        };
    }

    private static string[] NormalizeOrigins(IEnumerable<string>? origins) =>
        (origins ?? [])
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin.Trim().TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

/// <summary>Companion EF model for the OpenIddict vendor tables used by the demo feature.</summary>
public sealed class DemoOpenIddictDbContext(DbContextOptions<DemoOpenIddictDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict();
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Creates the portable demo token store when the shell generation activates.</summary>
public sealed class DemoOpenIddictStoreInitializer(IServiceProvider services) : IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DemoOpenIddictDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
