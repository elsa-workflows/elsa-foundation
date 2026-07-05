using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;

/// <summary>
/// Replaces the in-memory ASP.NET Core Identity stores with a durable EF Core store over
/// <see cref="ApplicationIdentityDbContext"/> and wires ASP.NET Core Identity core + cookie sign-in. When
/// <see cref="IsDevelopmentOrDemo"/> is set, an EF in-memory database is used and an admin account is seeded.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore",
    DisplayName = "Foundation Identity ASP.NET Core Identity EF Core",
    Description = "Durable EF Core persistence and SignInManager-backed cookie sign-in for the ASP.NET Core Identity provider."
)]
public sealed class AspNetCoreIdentityEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Development or demo", Description = "Uses an in-memory database and seeds an admin account for a fresh checkout.", Category = "Identity", DefaultValue = "false")]
    public bool IsDevelopmentOrDemo { get; set; }

    [ManifestSetting(DisplayName = "Connection string", Description = "Sqlite connection string for the identity database.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var connectionString = ConnectionString;
        var isDev = IsDevelopmentOrDemo;

        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            isDevelopmentOrDemo: isDev,
            configureDbContext: string.IsNullOrWhiteSpace(connectionString)
                ? null
                : builder => builder.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ApplicationIdentityDbContext).Assembly.GetName().Name)));
    }
}
