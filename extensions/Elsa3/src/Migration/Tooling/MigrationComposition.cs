using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa3.Migration.Tooling;

/// <summary>
/// The migration host's composition, built in process for one run (spec 1237 research R-4, R-5, T009): an empty generic
/// host that is never started, with the host folder as content root and the Development environment, configured through
/// the host's own <c>AddHostConfiguration</c> (behind a key filter), the Elsa 3 addition list and the run-override layer,
/// and served by the host's own <c>AddHostServices</c>. Data Protection is replaced with an ephemeral key ring. The shell
/// is activated here, so its initializers run; with <c>StartupTasksOnly</c> no background or recurring task starts.
/// Every EF module shares one in-memory SQLite database, which the composition holds open until it is disposed.
/// </summary>
internal sealed class MigrationComposition : IAsyncDisposable
{
    /// <summary>The addition list's file name in the CLI project folder (T013).</summary>
    public const string AdditionListFileName = "elsa3-migration.features.json";

    /// <summary>The environment the host runs in (spec 1236 R-2).</summary>
    public const string Environment = "Development";

    private const string ShellsSection = "CShells:Shells";
    private const string WorkflowReconciliation = "JsonWorkflowReconciliation";
    private const string ActivityReconciliation = "JsonActivityReconciliation";
    private const string Locking = "FileSystemDistributedLocking";
    private const string DataProtectionStore = "Elsa:DataProtection:EntityFrameworkCore";

    private readonly IHost _host;
    private readonly SqliteConnection _keepAlive;
    private readonly string _runFolder;

    private MigrationComposition(IHost host, IShell shell, SqliteConnection keepAlive, string runFolder, CompositionFacts facts)
    {
        _host = host;
        Shell = shell;
        _keepAlive = keepAlive;
        _runFolder = runFolder;
        Facts = facts;
    }

    /// <summary>The composed shell.</summary>
    public IShell Shell { get; }

    /// <summary>What the report's run section says about the composition.</summary>
    public CompositionFacts Facts { get; }

    /// <summary>The root services, for the tests.</summary>
    internal IServiceProvider RootServices => _host.Services;

    /// <summary>
    /// Composes and activates the shell. Returns the <c>composition-failed</c> refusal when a listed feature is missing or
    /// throws while the shell is composed (T026); then nothing is left behind.
    /// </summary>
    public static async Task<(MigrationComposition? Composition, MigrationRefusal? Refusal)> ComposeAsync(
        MigrationCommand command,
        HostWiring wiring,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wiring);
        var runId = Guid.NewGuid().ToString("N");
        var runFolder = Path.Combine(Path.GetTempPath(), "elsa3-migration-" + runId);
        var connectionString = $"Data Source=file:elsa3-migration-{runId};Mode=Memory;Cache=Shared";
        SqliteConnection? keepAlive = null;
        IHost? host = null;
        try
        {
            var workflows = Path.Combine(runFolder, "workflows");
            Directory.CreateDirectory(workflows);
            var activities = Path.Combine(runFolder, "activities.json");
            File.WriteAllText(activities, "[]");
            keepAlive = new SqliteConnection(connectionString);
            keepAlive.Open();

            var assemblies = ComposedAssemblies();
            var (layers, facts) = Configure(command, wiring, assemblies, connectionString, new RunFolders(runFolder, workflows, activities));

            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = wiring.HostProjectFolder,
                EnvironmentName = Environment,
                DisableDefaults = true
            });
            foreach (var layer in layers)
                builder.Configuration.AddConfiguration(layer);

            wiring.AddServices(builder);
            ReplaceDataProtection(builder.Services);

            host = builder.Build();
            var shell = await ActivateAsync(host.Services, facts.ShellName, cancellationToken);
            if (shell.Refusal is not null)
            {
                await DisposeAsync(host, keepAlive, runFolder);
                return (null, shell.Refusal);
            }

            return (new MigrationComposition(host, shell.Shell!, keepAlive, runFolder, facts), null);
        }
        catch (CompositionConfigurationException exception)
        {
            await DisposeAsync(host, keepAlive, runFolder);
            return (null, new(MigrationRefusal.CompositionFailed, exception.Sentence));
        }
        catch
        {
            await DisposeAsync(host, keepAlive, runFolder);
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeAsync(_host, _keepAlive, _runFolder);

    private static async Task<(IShell? Shell, MigrationRefusal? Refusal)> ActivateAsync(IServiceProvider root, string shellName, CancellationToken cancellationToken)
    {
        var configuration = root.GetRequiredService<IConfiguration>();
        var listed = configuration.GetSection($"{ShellsSection}:{shellName}:Features").GetChildren().Select(feature => feature.Key).ToArray();
        try
        {
            return (await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName, cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, new(MigrationRefusal.CompositionFailed, DescribeFailure(exception, listed)));
        }
    }

    /// <summary>
    /// Names the feature the failure is about, never a setting value or the exception's message (T026): a missing feature by
    /// its name, otherwise the listed features the exception chain names, otherwise the exception's type.
    /// </summary>
    internal static string DescribeFailure(Exception exception, IReadOnlyCollection<string> listed)
    {
        var chain = new List<Exception>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
            if (current is AggregateException aggregate)
                chain.AddRange(aggregate.InnerExceptions);
        }

        var missing = chain.OfType<FeatureNotFoundException>().Select(found => found.FeatureName).Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            return $"The composition lists feature(s) no project the migration host references provides: {string.Join(", ", missing)}. " +
                   "Add the feature's project to Elsa.Migration.Host.csproj, or remove it from the composition.";

        var named = listed
            .Where(feature => chain.Any(failure => NamesFeature(failure.Message, feature)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return named.Length > 0
            ? $"The shell could not be composed; the failure names the listed feature(s) {string.Join(", ", named)}. Check their settings, or run the migration host to see the cause."
            : $"The shell could not be composed ({chain[^1].GetType().Name}). Run the migration host with the same settings to see the cause.";
    }

    /// <summary>Whether <paramref name="message"/> names <paramref name="feature"/> as a whole word.</summary>
    private static bool NamesFeature(string message, string feature)
    {
        for (var index = message.IndexOf(feature, StringComparison.Ordinal); index >= 0; index = message.IndexOf(feature, index + 1, StringComparison.Ordinal))
        {
            var before = index == 0 || !char.IsLetterOrDigit(message[index - 1]);
            var end = index + feature.Length;
            var after = end == message.Length || !char.IsLetterOrDigit(message[end]);
            if (before && after)
                return true;
        }

        return false;
    }

    /// <summary>The three configuration layers of T009, lowest first, and what the report records about them.</summary>
    private static (IReadOnlyList<IConfiguration> Layers, CompositionFacts Facts) Configure(
        MigrationCommand command,
        HostWiring wiring,
        IReadOnlyList<Assembly> assemblies,
        string connectionString,
        RunFolders folders)
    {
        // 1. The host's layers behind the key filter. inventory reads only the committed files.
        var host = Filtered(Build(builder => wiring.AddConfiguration(builder, wiring.HostProjectFolder, Environment, command == MigrationCommand.Assess)));

        // 2. The addition list, from the CLI project folder only.
        var additionList = Path.Combine(wiring.CliProjectFolder, AdditionListFileName);
        if (!File.Exists(additionList))
            throw new CompositionConfigurationException($"The Elsa 3 addition list '{additionList}' is missing.");
        var additions = Build(builder => builder.AddJsonFile(additionList, optional: false, reloadOnChange: false));

        var composed = new ConfigurationBuilder().AddConfiguration(host).AddConfiguration(additions).Build();
        var shellName = composed.GetSection(ShellsSection).GetChildren().Select(shell => shell.Key).FirstOrDefault()
                        ?? throw new CompositionConfigurationException("The migration host's configuration composes no shell.");
        var featuresPath = $"{ShellsSection}:{shellName}:Features";
        var listed = composed.GetSection(featuresPath).GetChildren().Select(feature => feature.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 3. The run-override layer.
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = nameof(EfMigratePolicy.AutoMigrate)
        };
        var replaced = false;
        foreach (var name in ConnectionNames(composed, featuresPath, assemblies))
        {
            replaced |= composed[$"ConnectionStrings:{name}"] is not null;
            overrides[$"ConnectionStrings:{name}"] = connectionString;
        }

        foreach (var feature in EfFeatures(assemblies).Where(listed.Contains))
        {
            replaced |= composed.GetSection($"{featuresPath}:{feature}").GetChildren().Any(setting => IsConnectionSetting(setting.Key));
            overrides[$"{featuresPath}:{feature}:Provider"] = "Sqlite";
            overrides[$"{featuresPath}:{feature}:ConnectionString"] = connectionString;
        }

        if (composed.GetSection(DataProtectionStore).GetChildren().Any())
        {
            replaced = true;
            overrides[$"{DataProtectionStore}:Provider"] = "Sqlite";
            overrides[$"{DataProtectionStore}:ConnectionString"] = connectionString;
        }

        if (listed.Contains(WorkflowReconciliation))
            overrides[$"{featuresPath}:{WorkflowReconciliation}:Options:FolderPath"] = folders.Workflows;
        if (listed.Contains(ActivityReconciliation))
            overrides[$"{featuresPath}:{ActivityReconciliation}:Options:FilePath"] = folders.Activities;
        if (listed.Contains("Tasks"))
            overrides[$"{featuresPath}:Tasks:StartupTasksOnly"] = "true";
        if (listed.Contains(Locking))
            overrides[$"{featuresPath}:{Locking}:LocksFolderPath"] = Path.Combine(folders.Root, "locks");

        var settingKeys = composed.AsEnumerable()
            .Where(setting => setting.Value is not null && IsRecordedKey(setting.Key, shellName))
            .Select(setting => setting.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var facts = new CompositionFacts(shellName, listed.Order(StringComparer.Ordinal).ToArray(), settingKeys, replaced);
        return ([host, additions, new ConfigurationBuilder().AddInMemoryCollection(overrides).Build()], facts);
    }

    /// <summary>
    /// Every connection name an EF module may read: each module's <c>DefaultConnectionName</c> over the composed assemblies
    /// (the Elsa 3 import ledger's is one the host never names), each <c>ConnectionStrings</c> entry the configuration holds,
    /// and each <c>ConnectionName</c> a listed feature sets.
    /// </summary>
    private static IEnumerable<string> ConnectionNames(IConfiguration composed, string featuresPath, IReadOnlyList<Assembly> assemblies)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in EfModuleCatalog.Discover(assemblies))
            names.Add(module.DefaultConnectionName);
        foreach (var entry in composed.GetSection("ConnectionStrings").GetChildren())
            names.Add(entry.Key);
        foreach (var feature in composed.GetSection(featuresPath).GetChildren())
        {
            if (feature[nameof(EfConnectionDefaults.ConnectionName)] is { Length: > 0 } connectionName)
                names.Add(connectionName);
        }

        if (composed[$"{DataProtectionStore}:ConnectionName"] is { Length: > 0 } dataProtection)
            names.Add(dataProtection);
        return names;
    }

    private static bool IsConnectionSetting(string key) =>
        key.Equals("Provider", StringComparison.OrdinalIgnoreCase)
        || key.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase)
        || key.Equals("ConnectionName", StringComparison.OrdinalIgnoreCase);

    /// <summary>The names of the shell features that carry an EF connection (a <c>ConnectionString</c> setting).</summary>
    private static IEnumerable<string> EfFeatures(IReadOnlyList<Assembly> assemblies) =>
        assemblies
            .SelectMany(LoadableTypes)
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ShellFeatureAttribute>()))
            .Where(candidate => candidate.Attribute?.Name is { Length: > 0 }
                                && candidate.Type.GetProperty("ConnectionString", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true }
                                && candidate.Type.GetProperty("Provider", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true })
            .Select(candidate => candidate.Attribute!.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    /// <summary>The project assemblies of this process's build, which <c>WithHostAssemblies()</c> discovers features in.</summary>
    private static IReadOnlyList<Assembly> ComposedAssemblies()
    {
        var context = DependencyContext.Default;
        if (context is null)
            return AppDomain.CurrentDomain.GetAssemblies();

        var assemblies = new List<Assembly>();
        foreach (var library in context.RuntimeLibraries.Where(library => library.Type == "project"))
        {
            foreach (var name in library.GetDefaultAssemblyNames(context))
            {
                try
                {
                    assemblies.Add(Assembly.Load(name));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                }
            }
        }

        return assemblies;
    }

    /// <summary>The keys the report records (FR-018 item 4): under the host's shell section and <c>ConnectionStrings</c>.</summary>
    private static bool IsRecordedKey(string key, string shellName) =>
        key.StartsWith($"{ShellsSection}:{shellName}:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase);

    private static IConfigurationRoot Build(Action<IConfigurationBuilder> configure)
    {
        var builder = new ConfigurationBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// The host's settings without the reconciliation inputs a local source could add (T009): <c>Files</c>,
    /// <c>FilePath</c> and <c>AdditionalSources</c> of JSON workflow reconciliation, and <c>Files</c> of JSON activity
    /// reconciliation. The run sets its own <c>FolderPath</c> and <c>FilePath</c>.
    /// </summary>
    internal static IConfiguration Filtered(IConfiguration configuration)
    {
        // A feature listed with no settings ("Serialization": {}) is a key with a null value and no children; it is kept,
        // or the feature would drop out of the composition.
        var kept = configuration.AsEnumerable()
            .Where(setting => !IsFilteredKey(setting.Key)
                              && (setting.Value is not null || !configuration.GetSection(setting.Key).GetChildren().Any()))
            .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);
        return new ConfigurationBuilder().AddInMemoryCollection(kept).Build();
    }

    internal static bool IsFilteredKey(string key)
    {
        var parts = key.Split(':');
        // CShells:Shells:<shell>:Features:<feature>[:Options]:<setting>[...]
        if (parts.Length < 6
            || !parts[0].Equals("CShells", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("Shells", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("Features", StringComparison.OrdinalIgnoreCase))
            return false;

        var setting = parts.Length > 6 && parts[5].Equals("Options", StringComparison.OrdinalIgnoreCase) ? parts[6] : parts[5];
        if (parts[4].Equals(WorkflowReconciliation, StringComparison.OrdinalIgnoreCase))
            return setting.Equals("Files", StringComparison.OrdinalIgnoreCase)
                   || setting.Equals("FilePath", StringComparison.OrdinalIgnoreCase)
                   || setting.Equals("AdditionalSources", StringComparison.OrdinalIgnoreCase);
        return parts[4].Equals(ActivityReconciliation, StringComparison.OrdinalIgnoreCase)
               && setting.Equals("Files", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the host's Data Protection with an ephemeral key ring (T009): the run protects nothing that outlives it, and
    /// reads and writes no key store. The descriptors removed are those T015's parity comparison excludes.
    /// </summary>
    internal static void ReplaceDataProtection(IServiceCollection services)
    {
        var removed = services.Where(descriptor =>
                descriptor.ServiceType == typeof(IDataProtectionProvider)
                || descriptor.ServiceType == typeof(IKeyManager)
                || descriptor.ServiceType == typeof(IConfigureOptions<KeyManagementOptions>)
                || (descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name == "DataProtectionStartupCheck"))
            .ToArray();
        foreach (var descriptor in removed)
            services.Remove(descriptor);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    }

    private static async Task DisposeAsync(IHost? host, SqliteConnection? keepAlive, string runFolder)
    {
        try
        {
            if (host is IAsyncDisposable asyncHost)
                await asyncHost.DisposeAsync();
            else
                host?.Dispose();
        }
        finally
        {
            if (keepAlive is not null)
                await keepAlive.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(runFolder))
                    Directory.Delete(runFolder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record RunFolders(string Root, string Workflows, string Activities);

    /// <summary>A setting the composition cannot be built from; its sentence names a path or key, never a value.</summary>
    private sealed class CompositionConfigurationException(string sentence) : Exception(sentence)
    {
        public string Sentence { get; } = sentence;
    }
}

/// <summary>What the report's run section records about the composition (FR-018 item 4).</summary>
/// <param name="Features">The listed features, sorted.</param>
/// <param name="SettingKeys">The keys under the host's shell section and <c>ConnectionStrings</c>, sorted; never values.</param>
/// <param name="PersistenceOverridden">Whether a persistence connection setting was replaced for the run (FR-009).</param>
internal sealed record CompositionFacts(string ShellName, IReadOnlyList<string> Features, IReadOnlyList<string> SettingKeys, bool PersistenceOverridden);
