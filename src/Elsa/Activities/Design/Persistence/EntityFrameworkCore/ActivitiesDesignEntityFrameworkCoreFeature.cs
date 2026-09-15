using Elsa.Persistence.EntityFramework;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

[ShellFeature(name: "ActivitiesDesignEntityFrameworkCore", DisplayName = "Activities Design Entity Framework Core Persistence", Description = "Opt-in EF Core persistence for Activities Design.")]
public sealed class ActivitiesDesignEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Sqlite, SqlServer, PostgreSql, or MySql.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional provider connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Optional named connection.", Category = "Persistence")]
    public string? ConnectionName { get; set; }
    public void ConfigureServices(IServiceCollection services) => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName })
        .AddEfModuleMigrations<ActivitiesDesignDbContext>(Provider);
}
