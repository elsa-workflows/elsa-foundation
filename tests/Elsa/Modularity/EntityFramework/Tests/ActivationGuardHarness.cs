using Elsa.Modularity.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Elsa.Modularity.EntityFramework.Tests;

/// <summary>
/// The four real module/feature assemblies these tests evaluate against, handed to the guard explicitly
/// rather than through <see cref="LoadedEfModuleAssemblySource"/>, so a test states exactly which modules
/// the host it stands for has loaded.
/// </summary>
internal sealed class FixedEfModuleAssemblySource(params Assembly[] assemblies) : IEfModuleAssemblySource
{
    public IReadOnlyList<Assembly> GetAssemblies() => assemblies;
}

/// <summary>
/// Every log line written through the container the guard resolves from, so a test can assert that a
/// credential reached none of them (FR-061) rather than assuming it did not.
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Sink(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Sink(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// The shared setup every activation-guard test needs: a scratch directory for SQLite files, a container
/// carrying the host-wide migrate policy, and the request shapes the guard is asked about.
/// </summary>
internal sealed class ActivationGuardHarness : IDisposable
{
    public const string SecretsFeature = "SecretsEntityFrameworkCore";
    public const string RuntimeFeature = "WorkflowsRuntimeEntityFrameworkCore";
    public const string DashboardFeature = "WorkflowsDashboardEntityFrameworkCore";

    /// <summary>What a host running the guard would have loaded: the modules, and the features that map to them.</summary>
    public static readonly Assembly[] Assemblies =
    [
        typeof(SecretsEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
        typeof(RuntimeEntityFrameworkCoreFeature).Assembly
    ];

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("elsa-activation-guard");

    public CapturedLogs Logs { get; } = new();

    /// <summary>A SQLite file path under this test's own directory; the file itself is not created.</summary>
    public string Database(string name) => Path.Join(_root.FullName, $"{name}.db");

    /// <summary>A file that exists and is not a database, so reading its migration history throws.</summary>
    public string UnreadableDatabase(string name)
    {
        var path = Database(name);
        File.WriteAllText(path, "not a sqlite database");
        return path;
    }

    public EfPendingMigrationActivationGuard Guard(
        EfMigratePolicy policy,
        IDictionary<string, string?>? configuration = null,
        params Assembly[] assemblies)
    {
        var settings = new Dictionary<string, string?>(configuration ?? new Dictionary<string, string?>())
        {
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = policy.ToString()
        };

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddLogging(builder => builder.AddProvider(Logs))
            .BuildServiceProvider();

        return new(services, new FixedEfModuleAssemblySource(assemblies.Length > 0 ? assemblies : Assemblies));
    }

    public static FeatureActivationContext Request(params FeatureApplyItem[] features) =>
        new(
            new ShellFeatureConfigurationSnapshot("default", "revision-1", new Dictionary<string, JsonElement>()),
            new FeatureApplyRequest("revision-1", features));

    public static FeatureApplyItem Enabled(string feature, string provider = "Sqlite", string? connection = null, string? connectionName = null) =>
        new(feature, true, Configuration(provider, connection, connectionName));

    public static FeatureApplyItem Disabled(string feature, string provider = "Sqlite", string? connection = null) =>
        new(feature, false, Configuration(provider, connection, connectionName: null));

    /// <summary>A feature with no persistence settings of its own: the dashboard shape (FR-065).</summary>
    public static FeatureApplyItem EnabledWithoutSettings(string feature) =>
        new(feature, true, JsonSerializer.SerializeToElement(new Dictionary<string, string?>()));

    /// <summary>Brings <paramref name="module"/>'s SQLite schema current the way an operator would, out of process.</summary>
    public static async Task ApplyMigrationsAsync(string module, string connection)
    {
        var descriptor = EfModuleCatalog.Find(EfModuleCatalog.Discover(Assemblies), module)!;
        var contextType = descriptor.RequireProviderContext("Sqlite");
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        EfRelationalProviderBinding.UseSqlite(builder, connection, descriptor.HistoryTableName, descriptor.Assembly.GetName().Name);
        await using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        await context.Database.MigrateAsync();
    }

    public static string ConnectionTo(string path) => $"Data Source={path}";

    public void Dispose()
    {
        // The pool holds the files open, and a pooled handle outlives the context that opened it.
        SqliteConnection.ClearAllPools();
        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover scratch file is not a test failure.
        }
    }

    private static JsonElement Configuration(string provider, string? connection, string? connectionName) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string?>
        {
            ["Provider"] = provider,
            ["ConnectionString"] = connection,
            ["ConnectionName"] = connectionName
        });
}
