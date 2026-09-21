using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Secrets")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "SecretsEntityFrameworkCore",
    DisplayName = "Secrets Entity Framework Core Persistence",
    Description = "EF Core persistence for the secrets repository."
)]
public class SecretsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the derived Secrets DbContext: Sqlite, SqlServer, PostgreSql, or MySql. The host must reference that provider package.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "Optional explicit connection string. When omitted, ConnectionName or the shared ConnectionStrings:Elsa is used. Sqlite defaults to Data Source=elsa.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    [ManifestSetting(
        DisplayName = "Connection name",
        Description = "Named connection under ConnectionStrings when ConnectionString is omitted.",
        Category = "Persistence")]
    public string? ConnectionName { get; set; }

    [ManifestSetting(
        DisplayName = "Schema",
        Description = "Optional database schema for this module's tables and its own migrations history table. Falls back to Elsa:Persistence:EntityFramework:Schema, then to the provider's own default. Ignored on Sqlite, which has no schemas, and refused on MySql, where a schema is a database: name it in the connection string there instead.",
        Category = "Persistence")]
    public string? Schema { get; set; }

    [ManifestSetting(
        DisplayName = "Pooled contexts",
        Description = "Reuse DbContext instances from a pool instead of constructing one per scope. Safe for every first-party module context, which carries nothing but its options.",
        Category = "Persistence")]
    public bool Pooling { get; set; }

    /// <summary>
    /// Retired (ADR 0076 D8, FR-058). Secrets now reads the host-wide
    /// <c>Elsa:Persistence:EntityFramework:Migrate:Policy</c> like every other module, and a shell that still
    /// sets this refuses to start.
    /// </summary>
    /// <remarks>
    /// Kept for one release, nullable and ordinarily settable, because neither of the two obvious removals
    /// fails loudly under CShells' binder. Deleting the property is silent: <c>AutoBindFeatureProperties</c>
    /// only looks at a configuration key when a property of that exact name still exists, so a still-configured
    /// value would never be read at all. A throwing setter is silent too: <c>BindProperty</c> wraps every set
    /// in a try/catch that only logs a warning. The type is <see cref="string"/> rather than the enum so that
    /// <i>any</i> configured value binds — a value the enum cannot parse would otherwise throw inside that same
    /// swallowed set and vanish. The refusal itself lives in <see cref="ConfigureServices"/>, which
    /// <c>ShellProviderBuilder</c> does not swallow (it rethrows as a feature-configuration failure), unlike
    /// <c>ApplyConfiguration</c>, which only surfaces <c>FeatureConfigurationValidationException</c>.
    /// </remarks>
    [Obsolete("Set Elsa:Persistence:EntityFramework:Migrate:Policy instead. A non-null value here refuses to start.")]
    [ManifestSetting(
        DisplayName = "Migrate policy (retired)",
        Description = "Retired. Set the host-wide Elsa:Persistence:EntityFramework:Migrate:Policy instead; a shell that still sets this one refuses to start.",
        Category = "Persistence")]
    public string? MigratePolicy { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        RefuseRetiredMigratePolicy();
        services.AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
        {
            Provider = Provider,
            ConnectionString = ConnectionString,
            ConnectionName = ConnectionName,
            Schema = Schema,
            Pooling = Pooling
        });
    }

    private void RefuseRetiredMigratePolicy()
    {
#pragma warning disable CS0618 // The refusal is the whole point of keeping the obsolete property.
        if (MigratePolicy is null)
            return;

        throw new InvalidOperationException(
            $"'SecretsEntityFrameworkCore:{nameof(MigratePolicy)}' is set to '{MigratePolicy}' and is retired. " +
            $"Secrets reads the host-wide '{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}' key " +
            "every other EF module already reads, so it can no longer be set independently within one shell. " +
            $"Move the value to '{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}' — in this shell's " +
            "own Configuration node when this shell needs a different policy from its neighbours — and remove this setting.");
#pragma warning restore CS0618
    }
}
