using System.Reflection;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>Every provider-derived context declared by a first-party EF module.</summary>
internal static class ModuleContextCatalog
{
    public static readonly string[] Providers = ["Sqlite", "SqlServer", "PostgreSql", "MySql"];

    // One anchor per module assembly; a module that gains a context is found without further edits.
    private static readonly Assembly[] Modules =
    [
        typeof(ActivitiesDesignDbContext).Assembly,
        typeof(OpenTelemetryDbContext).Assembly,
        typeof(StructuredLogsDbContext).Assembly,
        typeof(IdentityIamDbContext).Assembly,
        typeof(SecretsDbContext).Assembly,
        typeof(StudioPreferencesDbContext).Assembly,
        typeof(WorkflowsDesignDbContext).Assembly,
        typeof(PublishingSnapshotReviewDbContext).Assembly,
        typeof(ExecutionPlacementDbContext).Assembly,
        typeof(BookmarkStateDbContext).Assembly,
        typeof(Elsa3ImportDbContext).Assembly
    ];

    public static IReadOnlyList<Type> Contexts(string provider) => Modules
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false } && typeof(DbContext).IsAssignableFrom(type) &&
                       type.Name.EndsWith(provider + "DbContext", StringComparison.Ordinal))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<Type> AllContexts() => Providers.SelectMany(Contexts).ToArray();

    public static string ProviderOf(Type context) =>
        Providers.Single(provider => context.Name.EndsWith(provider + "DbContext", StringComparison.Ordinal));

    /// <summary>Each module context keeps its own history table, so modules can share one database.</summary>
    public static string HistoryTable(Type context) =>
        EfMigrationsHistory.TableName(context.Name[..^(ProviderOf(context).Length + "DbContext".Length)]);

    /// <summary>A connection string the provider parses but nothing ever opens; building a model needs no database.</summary>
    public static string PlaceholderConnection(string provider) => provider switch
    {
        "Sqlite" => "Data Source=:memory:",
        "SqlServer" => "Server=localhost;Database=elsa;TrustServerCertificate=True",
        "PostgreSql" => "Host=localhost;Database=elsa",
        "MySql" => "Server=localhost;Database=elsa",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public static DbContext Create(Type context, string connectionString, Action<DbContextOptionsBuilder>? configure = null, string? schema = null)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(context))!;
        EfRelationalProviderBinding.Use(builder, ProviderOf(context), connectionString, HistoryTable(context), context.Assembly.GetName().Name, schema);
        configure?.Invoke(builder);
        return (DbContext)Activator.CreateInstance(context, builder.Options)!;
    }

    /// <summary>Applies every module's migrations to one database, then proves each is current and isolated.</summary>
    public static async Task InstallAllAsync(string provider, string connectionString, string? schema = null)
    {
        var contexts = Contexts(provider);
        foreach (var type in contexts)
        {
            await using var context = Create(type, connectionString, schema: schema);
            await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.AutoMigrate);
        }

        foreach (var type in contexts)
        {
            await using var context = Create(type, connectionString, schema: schema);
            await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate);
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            if (!applied.SequenceEqual(context.Database.GetMigrations()))
                throw new InvalidOperationException($"{type.Name} history does not match its own migration set: {string.Join(", ", applied)}.");
        }
    }
}
