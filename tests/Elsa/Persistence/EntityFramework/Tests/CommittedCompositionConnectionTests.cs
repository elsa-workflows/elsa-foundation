using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// A committed composition names a provider per EF feature but almost never names a connection, so every
/// entry lands on <see cref="EfConnectionDefaults.ResolveConnectionString"/>'s fallbacks. That is only safe
/// while the composition and the modules agree on what those fallbacks are, and they can disagree two ways:
/// a module whose default connection name is not the shared <c>Elsa</c> finds nothing when a stack supplies
/// only the shared entry, and a feature left on the default Sqlite provider picks up the shared entry even
/// when it holds another engine's connection string. Both fail at shell activation, not at review, so these
/// guards resolve every EF feature in every committed composition through the real resolver.
/// </summary>
public sealed partial class CommittedCompositionConnectionTests
{
    // Declared first: the static scans below run in declaration order and every one of them reads it.
    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>
    /// The modules that resolve an unsupplied connection under a name other than
    /// <see cref="EfConnectionDefaults.ConnectionName"/>, keyed by the feature that composes them.
    /// <see cref="Every_module_that_deviates_from_the_shared_connection_name_is_mapped_to_its_features"/>
    /// keeps this in step with the <c>DefaultConnectionName</c> constants under <c>src/</c> and
    /// <c>extensions/</c>; every other EF feature resolves under the shared name.
    /// </summary>
    private static readonly Dictionary<string, string> DeviatingFeatureConnectionNames = new(StringComparer.Ordinal)
    {
        ["DiagnosticsOpenTelemetryEntityFrameworkCore"] = "ElsaOpenTelemetry",
        ["Elsa3ImportActivitiesEntityFrameworkCore"] = "ElsaElsa3Import"
    };

    // The compose stacks run in Production, where shells.Production.json sits above the composition the stack
    // mounts and adds features of its own. Resolve the same layering WorkbenchConfigurationTests does, so the
    // features these guards see are the ones the container composes.
    [Theory]
    [InlineData("docker-compose.yml", "docker/compose/elsa-workbench.shells.json")]
    [InlineData("docker-compose.images.yml", "src/Apps/Elsa.Workbench/shells.json")]
    public void Every_ef_feature_a_production_compose_stack_composes_resolves_a_connection(string composeFile, string shellsJson)
    {
        var environment = ReadWorkbenchEnvironment(Path.Join(RepoRoot, "docker", "compose", composeFile));
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Join(RepoRoot, shellsJson))
            .AddJsonFile(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench", "shells.Production.json"))
            .AddInMemoryCollection(environment.Select(entry => KeyValuePair.Create(entry.Key.Replace("__", ":"), (string?)entry.Value)))
            .Build();

        AssertEveryEfFeatureResolves(configuration, $"{composeFile} over {shellsJson}");
    }

    [Fact]
    public void Every_ef_feature_the_workbench_composes_by_default_resolves_a_connection()
    {
        var shellsJson = Path.Join("src", "Apps", "Elsa.Workbench", "shells.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Join(RepoRoot, shellsJson)).Build();

        AssertEveryEfFeatureResolves(configuration, shellsJson);
    }

    [Fact]
    public void Every_module_that_deviates_from_the_shared_connection_name_is_mapped_to_its_features()
    {
        var declared = SourceFiles()
            .SelectMany(file => DeviatingDefaultConnectionName().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            DeviatingFeatureConnectionNames.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            declared);
    }

    /// <summary>
    /// An operator configures a connection from what a feature's settings say, and six features used to name a
    /// per-module <c>ConnectionStrings</c> entry their module had stopped resolving. Naming the wrong entry
    /// sends a host down exactly the path this class guards, so a description may only spell out the entry its
    /// own module falls back to. A description that contrasts itself with another module's entry says so in
    /// words rather than writing that entry's <c>ConnectionStrings:</c> path, which reads as an instruction.
    /// </summary>
    [Fact]
    public void No_ef_feature_setting_names_a_connection_entry_its_module_does_not_resolve()
    {
        var misnamed =
            from file in SourceFiles()
            let source = File.ReadAllText(file)
            let feature = ShellFeatureName().Match(source)
            where feature.Success && EfFeatures.ContainsKey(feature.Groups["name"].Value)
            let expected = DeviatingFeatureConnectionNames.GetValueOrDefault(feature.Groups["name"].Value, EfConnectionDefaults.ConnectionName)
            from named in NamedConnectionEntry().Matches(source)
            where named.Groups["name"].Value != expected
            select $"  {feature.Groups["name"].Value} names ConnectionStrings:{named.Groups["name"].Value}, but resolves ConnectionStrings:{expected}.";

        var failures = misnamed.Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            failures.Length == 0,
            "EF feature settings describe a connection entry their module does not resolve:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    // Composing an EF feature the catalog does not know about would silently skip it, so prove the scan finds
    // the module families a reference stack actually composes.
    [Theory]
    [InlineData("WorkflowsRuntimeEntityFrameworkCore")]
    [InlineData("DiagnosticsOpenTelemetryEntityFrameworkCore")]
    [InlineData("IdentityIamEntityFrameworkCore")]
    [InlineData("SecretsEntityFrameworkCore")]
    public void The_source_scan_finds_the_ef_features_a_reference_stack_composes(string feature) =>
        Assert.Contains(feature, EfFeatures.Keys);

    private static void AssertEveryEfFeatureResolves(IConfiguration configuration, string composition)
    {
        var services = new ConfigurationServices(configuration);
        var failures = configuration
            .GetSection("CShells:Shells:default:Features")
            .GetChildren()
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
        var provider = Blank(entry["Provider"]) ? EfFeatures[entry.Key] : entry["Provider"]!;
        string resolved;
        try
        {
            // The module's own Sqlite file only stands in when nothing is configured, and which file that is
            // cannot change whether the entry resolves, so the shared default stands in for every module here.
            resolved = EfConnectionDefaults.ResolveConnectionString(
                services,
                entry.Key,
                provider,
                entry["ConnectionString"],
                entry["ConnectionName"],
                DeviatingFeatureConnectionNames.GetValueOrDefault(entry.Key, EfConnectionDefaults.ConnectionName));
        }
        catch (InvalidOperationException exception)
        {
            return $"  {entry.Key}: {exception.Message}";
        }

        return EfRelationalProviderBinding.Normalize(provider) == "sqlite" && !IsSqliteConnectionString(resolved)
            ? $"  {entry.Key}: resolves the Sqlite provider onto '{resolved}', which Sqlite cannot open. " +
              "Name the provider this connection belongs to, or point the feature at a connection of its own."
            : null;
    }

    private static bool IsSqliteConnectionString(string connectionString)
    {
        try
        {
            _ = new SqliteConnectionStringBuilder(connectionString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Every first-party EF feature, mapped to the provider it selects when a composition names none.</summary>
    private static Dictionary<string, string> EfFeatures { get; } = ScanEfFeatures();

    private static Dictionary<string, string> ScanEfFeatures()
    {
        var features = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);
            var provider = ProviderSetting().Match(source);
            var name = ShellFeatureName().Match(source);
            // A feature that carries a provider, a connection string and a connection name is one that resolves
            // its own connection; the rest compose onto a context another feature already bound.
            if (provider.Success && name.Success && source.Contains("ConnectionName", StringComparison.Ordinal))
                features[name.Groups["name"].Value] = provider.Groups["provider"].Value;
        }

        return features;
    }

    private static IEnumerable<string> SourceFiles() =>
        new[] { "src", "extensions" }
            .Select(root => Path.Join(RepoRoot, root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    [GeneratedRegex("""ShellFeature\(\s*name:\s*"(?<name>[^"]+)""")]
    private static partial Regex ShellFeatureName();

    [GeneratedRegex("""public\s+string\s+Provider\s*\{\s*get;\s*set;\s*\}\s*=\s*"(?<provider>[^"]+)"\s*;""")]
    private static partial Regex ProviderSetting();

    [GeneratedRegex("""public\s+const\s+string\s+DefaultConnectionName\s*=\s*"(?<name>[^"]+)"\s*;""")]
    private static partial Regex DeviatingDefaultConnectionName();

    [GeneratedRegex("""ConnectionStrings:(?<name>[A-Za-z0-9_]+)""")]
    private static partial Regex NamedConnectionEntry();

    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
