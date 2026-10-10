using System.Reflection;
using CShells.Configuration;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;
using YamlDotNet.RepresentationModel;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A committed composition names a provider per EF feature but almost never names a connection, so every
/// entry lands on <see cref="EfConnectionDefaults.ResolveConnectionString"/>'s fallbacks. That is only safe
/// while the composition and the modules agree on what those fallbacks are, and they can disagree two ways:
/// a module whose default connection name is not the shared <c>Elsa</c> finds nothing when a stack supplies
/// only the shared entry, and a feature left on the default Sqlite provider picks up the shared entry even
/// when it holds another engine's connection string. Both fail at shell activation rather than at review, so
/// these guards resolve every EF feature the committed Workbench compositions compose — the two compose
/// stacks, <c>shells.json</c> and <c>shells.baseline.json</c> — through the real resolver.
/// </summary>
/// <remarks>
/// The catalog these guards resolve against is read from the compiled feature types and their
/// <see cref="UsesEfModuleAttribute"/> / <see cref="EfModuleAttribute"/> declarations, so a renamed feature, a
/// changed default provider or a changed <see cref="EfModuleAttribute.DefaultConnectionName"/> reaches the
/// resolution checks as it would reach a host. A feature the catalog cannot see would be skipped in silence;
/// <see cref="Every_entity_framework_feature_a_committed_composition_composes_is_in_the_catalog"/> makes that
/// loud, and it is derived from the compositions rather than from a list.
/// </remarks>
public sealed class CommittedCompositionConnectionTests
{
    /// <summary>
    /// Every EF feature that resolves a connection of its own, by feature name: the provider it selects when a
    /// composition names none, and the connection name it falls back to when a composition names no connection.
    /// </summary>
    private static IReadOnlyDictionary<string, EfFeature> EfFeatures => Catalog.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, EfFeature>> Catalog = new(BuildCatalog);

    private sealed record EfFeature(Type Type, string Provider, string ConnectionName);

    private static IReadOnlyDictionary<string, EfFeature> BuildCatalog()
    {
        var modules = RuntimeLibraries.Assemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<EfModuleAttribute>())
            .ToDictionary(module => module.Name, StringComparer.Ordinal);

        // A feature that carries both a provider and a connection name resolves its own connection; the rest either
        // compose onto a context another feature bound, or pin one engine and take a connection string only.
        return RuntimeLibraries.FeatureTypes
            .Where(type => IsStringSetting(type, "Provider") && IsStringSetting(type, "ConnectionName"))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .ToDictionary(
                ShellBuilder.ResolveFeatureName,
                type => new EfFeature(type, DefaultProvider(type), DefaultConnectionName(type, modules)),
                StringComparer.Ordinal);
    }

    private static bool IsStringSetting(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var propertyType, CanWrite: true } &&
        propertyType == typeof(string);

    /// <summary>The provider a fresh feature carries, or <c>Sqlite</c>, which every feature reads an unset value as.</summary>
    private static string DefaultProvider(Type feature) =>
        feature.GetProperty("Provider")!.GetValue(Activator.CreateInstance(feature)) is string { Length: > 0 } provider ? provider : "Sqlite";

    /// <summary>
    /// The connection name a feature falls back to: its EF module's <see cref="EfModuleAttribute.DefaultConnectionName"/>,
    /// else a <c>DefaultConnectionName</c> constant of its own, else <see cref="EfConnectionDefaults.ConnectionName"/>.
    /// A module name that matches no declaration, or two modules that disagree, fail the catalog rather than guess.
    /// </summary>
    private static string DefaultConnectionName(Type feature, IReadOnlyDictionary<string, EfModuleAttribute> modules)
    {
        var used = feature.GetCustomAttributes<UsesEfModuleAttribute>().Select(uses => uses.Module).ToArray();
        var unknown = used.Where(module => !modules.ContainsKey(module)).ToArray();
        Assert.True(unknown.Length == 0, $"{feature.FullName} uses EF module(s) no runtime library declares: {string.Join(", ", unknown)}.");

        var names = used.Select(module => modules[module].DefaultConnectionName).Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(names.Length <= 1, $"{feature.FullName} uses EF modules with different default connection names: {string.Join(", ", names)}.");

        return names.SingleOrDefault()
               ?? feature.GetField("DefaultConnectionName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
               ?? EfConnectionDefaults.ConnectionName;
    }

    /// <summary>
    /// The EF-named features that deliberately resolve no connection of their own, so a composition may list
    /// them without naming a provider. Each composes onto a context another feature in the same composition
    /// already bound, which is why <see cref="EfFeatures"/> does not carry them.
    /// </summary>
    private static readonly HashSet<string> ContextSharingFeatures = new(StringComparer.Ordinal)
    {
        "WorkflowsDashboardEntityFrameworkCore"
    };

    // The compose stacks run in Production, where shells.Production.json sits above the composition the stack
    // mounts and adds features of its own. WorkbenchConfigurationTests layers the same shell files and
    // environment; this adds appsettings.json underneath them, because connection resolution reads it and that
    // class does not care about connections.
    [Theory]
    [InlineData("docker-compose.yml", "docker/compose/elsa-workbench.shells.json")]
    [InlineData("docker-compose.images.yml", "src/apps/Elsa.Workbench/shells.json")]
    public void Every_ef_feature_a_production_compose_stack_composes_resolves_a_connection(string composeFile, string shellsJson) =>
        AssertEveryEfFeatureResolves(ComposeStack(composeFile, shellsJson), $"{composeFile} over {shellsJson}");

    // The compositions a host runs directly, with no overlay and no environment.
    [Theory]
    [InlineData("src/apps/Elsa.Workbench/shells.json")]
    [InlineData("src/apps/Elsa.Workbench/shells.baseline.json")]
    public void Every_ef_feature_a_committed_shell_composition_composes_resolves_a_connection(string shellsJson) =>
        AssertEveryEfFeatureResolves(
            WorkbenchConfiguration().AddJsonFile(Path.Join(RepoRoot, shellsJson)).Build(),
            shellsJson);

    /// <summary>
    /// These two are the Sqlite compositions, and <c>appsettings.json</c> underneath them supplies a Sqlite
    /// <c>ConnectionStrings:Elsa</c>. Resolution alone therefore cannot fail here: a feature switched to
    /// PostgreSql would quietly resolve <c>Data Source=elsa.db</c> and only die inside the provider at
    /// activation. Guard the property the files actually hold instead of inferring it from a resolution that
    /// no longer throws.
    /// </summary>
    [Theory]
    [InlineData("src/apps/Elsa.Workbench/shells.json")]
    [InlineData("src/apps/Elsa.Workbench/shells.baseline.json")]
    public void Every_ef_feature_a_committed_shell_composition_composes_selects_sqlite(string shellsJson)
    {
        var configuration = WorkbenchConfiguration().AddJsonFile(Path.Join(RepoRoot, shellsJson)).Build();
        var failures = Features(configuration)
            .Where(entry => EfFeatures.ContainsKey(entry.Key))
            .Select(entry => (entry.Key, Provider: string.IsNullOrWhiteSpace(entry["Provider"]) ? EfFeatures[entry.Key].Provider : entry["Provider"]!))
            .Where(entry => EfRelationalProviderBinding.Normalize(entry.Provider) != "sqlite")
            .Select(entry => $"  {entry.Key} selects {entry.Provider}.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            failures.Length == 0,
            $"{shellsJson} is a Sqlite composition, and the connection it falls back to is a Sqlite file. " +
            "A feature that selects another provider here needs a connection of that engine named on it, and a " +
            "guard that checks the pair — see the compose stacks:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every guard here filters composition entries through <see cref="EfFeatures"/>, so a feature the catalog
    /// cannot see is skipped rather than checked. This makes that silence loud: an EF-named entry must either
    /// resolve its own connection or be a known context-sharing feature.
    /// </summary>
    [Fact]
    public void Every_entity_framework_feature_a_committed_composition_composes_is_in_the_catalog()
    {
        var unknown = Compositions()
            .SelectMany(composition => Features(composition.Configuration).Select(entry => (composition.Name, entry.Key)))
            .Where(entry => entry.Key.Contains("EntityFrameworkCore", StringComparison.Ordinal)
                && !EfFeatures.ContainsKey(entry.Key)
                && !ContextSharingFeatures.Contains(entry.Key))
            .Select(entry => $"  {entry.Key} (composed by {entry.Name})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "These composed EF features resolve no connection here, because the catalog did not find them. Either the " +
            "feature lacks public string Provider/ConnectionName settings or a parameterless constructor, its library is not " +
            "referenced by this project, or it composes onto another feature's context, in which case add it to ContextSharingFeatures:" +
            Environment.NewLine + string.Join(Environment.NewLine, unknown));
    }

    private static IConfiguration ComposeStack(string composeFile, string shellsJson) =>
        WorkbenchConfiguration()
            .AddJsonFile(Path.Join(RepoRoot, shellsJson))
            .AddJsonFile(Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench", "shells.Production.json"))
            .AddInMemoryCollection(ReadWorkbenchEnvironment(Path.Join(RepoRoot, "docker", "compose", composeFile))
                .Select(entry => KeyValuePair.Create(entry.Key.Replace("__", ":"), (string?)entry.Value)))
            .Build();

    /// <summary>
    /// What the host has already layered before it reaches its shell files: <c>WebApplication.CreateBuilder</c>
    /// reads <c>appsettings.json</c> first, and that file supplies a <c>ConnectionStrings:Elsa</c> of its own.
    /// A guard that skipped it would resolve differently from the host it models — and would go red the day a
    /// deployment moved its connection string out of <c>environment:</c> and into an appsettings file.
    /// </summary>
    private static IConfigurationBuilder WorkbenchConfiguration() =>
        new ConfigurationBuilder().AddJsonFile(Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench", "appsettings.json"));

    private static IEnumerable<(string Name, IConfiguration Configuration)> Compositions()
    {
        yield return ("docker-compose.yml", ComposeStack("docker-compose.yml", "docker/compose/elsa-workbench.shells.json"));
        yield return ("docker-compose.images.yml", ComposeStack("docker-compose.images.yml", "src/apps/Elsa.Workbench/shells.json"));
        foreach (var shellsJson in new[] { "src/apps/Elsa.Workbench/shells.json", "src/apps/Elsa.Workbench/shells.baseline.json" })
            yield return (shellsJson, WorkbenchConfiguration().AddJsonFile(Path.Join(RepoRoot, shellsJson)).Build());
    }

    private static IEnumerable<IConfigurationSection> Features(IConfiguration configuration) =>
        configuration.GetSection("CShells:Shells:default:Features").GetChildren();

    private static void AssertEveryEfFeatureResolves(IConfiguration configuration, string composition)
    {
        var services = new ConfigurationServices(configuration);
        var failures = Features(configuration)
            .Where(entry => EfFeatures.ContainsKey(entry.Key))
            .Select(entry => Failure(services, entry))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            failures.Length == 0,
            $"{composition} composes EF features it does not supply a usable connection for:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static string? Failure(IServiceProvider services, IConfigurationSection entry)
    {
        var provider = string.IsNullOrWhiteSpace(entry["Provider"]) ? EfFeatures[entry.Key].Provider : entry["Provider"]!;
        var source = string.IsNullOrWhiteSpace(entry["ConnectionString"])
            ? $"ConnectionStrings:{(string.IsNullOrWhiteSpace(entry["ConnectionName"]) ? ConnectionNameFor(entry.Key) : entry["ConnectionName"]!)}"
            : "its ConnectionString";
        try
        {
            // The module's own Sqlite file only stands in when nothing is configured, and which file that is
            // cannot change whether the entry resolves, so the shared default stands in for every module here.
            var resolved = EfConnectionDefaults.ResolveConnectionString(
                services,
                entry.Key,
                provider,
                entry["ConnectionString"],
                entry["ConnectionName"],
                ConnectionNameFor(entry.Key));

            // Name the keyword Sqlite rejected rather than the connection string itself: these settings are
            // marked Secret, and a committed demo password has no business being echoed into a CI log.
            // Select is what a module itself calls, so an unknown provider is refused here exactly as it would
            // be at activation, rather than quietly failing the "is it Sqlite" comparison Normalize would allow.
            return EfRelationalProviderBinding.Select(provider, entry.Key, true, false, false, false) && SqliteRefusal(resolved) is { } refusal
                ? $"  {entry.Key}: selects the Sqlite provider, but {source} is not a Sqlite connection — {refusal} " +
                  "Name the provider that connection belongs to, or point the feature at a connection of its own."
                : null;
        }
        // The two a composition can provoke: the resolver refuses a connection it cannot find, and Select
        // refuses a provider name it does not know. Anything else is a defect in this test or in the code it
        // calls, and is left to surface as itself rather than be reported as a bad composition.
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return $"  {entry.Key}: {exception.Message}";
        }
    }

    private static string? SqliteRefusal(string connectionString)
    {
        try
        {
            _ = new SqliteConnectionStringBuilder(connectionString);
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return exception.Message;
        }
    }

    private static string ConnectionNameFor(string feature) => EfFeatures[feature].ConnectionName;

    private static Dictionary<string, string> ReadWorkbenchEnvironment(string composePath)
    {
        using var reader = File.OpenText(composePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var environment = Assert.IsType<YamlMappingNode>(yaml.Documents[0].RootNode["services"]["elsa-workbench"]["environment"]);
        return environment.Children.ToDictionary(
            entry => Assert.IsType<YamlScalarNode>(entry.Key).Value!,
            entry => Assert.IsType<YamlScalarNode>(entry.Value).Value!);
    }

    /// <summary>The only service <see cref="EfConnectionDefaults.ResolveConnectionString"/> asks for is <see cref="IConfiguration"/>.</summary>
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
