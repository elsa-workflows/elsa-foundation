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
/// when it holds another engine's connection string. Both fail at shell activation rather than at review, so
/// these guards resolve every EF feature the committed Workbench compositions compose — the two compose
/// stacks, <c>shells.json</c> and <c>shells.baseline.json</c> — through the real resolver.
/// </summary>
/// <remarks>
/// The catalog these guards resolve against is scanned out of source, so a feature it cannot see would be
/// skipped in silence — which is the one way this class could report green over the very regression it
/// exists for. <see cref="Every_entity_framework_feature_a_committed_composition_composes_is_in_the_catalog"/>
/// is what makes that case loud, and it is derived from the compositions rather than from a list, so it
/// fails when the scan misses something a stack actually composes.
/// </remarks>
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

    /// <summary>Every first-party source file, read once: the catalog and three guards below scan the same text.</summary>
    private static IReadOnlyList<SourceFile> Sources { get; } = new[] { "src", "extensions" }
        .Select(root => Path.Join(RepoRoot, root))
        .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Select(path => new SourceFile(Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path)))
        .ToArray();

    /// <summary>Every EF feature that resolves a connection of its own, and the provider it selects when a composition names none.</summary>
    private static IReadOnlyDictionary<string, string> EfFeatures { get; } = Sources
        .Select(source => (Source: source, Name: ShellFeatureName().Match(source.Text), Provider: ProviderSetting().Match(source.Text)))
        // A feature that carries both a provider and a connection name resolves its own connection; the rest
        // either compose onto a context another feature bound, or pin one engine and take a connection string only.
        .Where(candidate => candidate.Name.Success && candidate.Provider.Success && candidate.Source.Text.Contains("ConnectionName", StringComparison.Ordinal))
        // An uninitialised provider property is the feature's own "unset", which every such feature reads as Sqlite.
        .ToDictionary(
            candidate => candidate.Name.Groups["name"].Value,
            candidate => candidate.Provider.Groups["provider"].Success ? candidate.Provider.Groups["provider"].Value : "Sqlite",
            StringComparer.Ordinal);

    /// <summary>
    /// The EF-named features that deliberately resolve no connection of their own, so a composition may list
    /// them without naming a provider. Each composes onto a context another feature in the same composition
    /// already bound, which is why <see cref="EfFeatures"/> does not carry them.
    /// </summary>
    private static readonly HashSet<string> ContextSharingFeatures = new(StringComparer.Ordinal)
    {
        "WorkflowsDashboardEntityFrameworkCore"
    };

    /// <summary>
    /// The modules that resolve an unsupplied connection under a name other than
    /// <see cref="EfConnectionDefaults.ConnectionName"/>, keyed by the feature that composes them. Slice 2 of
    /// spec 171 (#1872) puts a default-connection member on <c>EfModuleDescriptor</c>; this table is what it
    /// replaces. Until then
    /// <see cref="Every_module_that_deviates_from_the_shared_connection_name_is_mapped_to_its_features"/>
    /// keeps it in step with the <c>DefaultConnectionName</c> constants under <c>src/</c> and
    /// <c>extensions/</c>; every other EF feature resolves under the shared name.
    /// </summary>
    private static readonly Dictionary<string, string> DeviatingFeatureConnectionNames = new(StringComparer.Ordinal)
    {
        ["DiagnosticsOpenTelemetryEntityFrameworkCore"] = "ElsaOpenTelemetry",
        ["Elsa3ImportActivitiesEntityFrameworkCore"] = "ElsaElsa3Import"
    };

    // The compose stacks run in Production, where shells.Production.json sits above the composition the stack
    // mounts and adds features of its own. WorkbenchConfigurationTests layers the same shell files and
    // environment; this adds appsettings.json underneath them, because connection resolution reads it and that
    // class does not care about connections.
    [Theory]
    [InlineData("docker-compose.yml", "docker/compose/elsa-workbench.shells.json")]
    [InlineData("docker-compose.images.yml", "src/Apps/Elsa.Workbench/shells.json")]
    public void Every_ef_feature_a_production_compose_stack_composes_resolves_a_connection(string composeFile, string shellsJson) =>
        AssertEveryEfFeatureResolves(ComposeStack(composeFile, shellsJson), $"{composeFile} over {shellsJson}");

    // The compositions a host runs directly, with no overlay and no environment.
    [Theory]
    [InlineData("src/Apps/Elsa.Workbench/shells.json")]
    [InlineData("src/Apps/Elsa.Workbench/shells.baseline.json")]
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
    [InlineData("src/Apps/Elsa.Workbench/shells.json")]
    [InlineData("src/Apps/Elsa.Workbench/shells.baseline.json")]
    public void Every_ef_feature_a_committed_shell_composition_composes_selects_sqlite(string shellsJson)
    {
        var configuration = WorkbenchConfiguration().AddJsonFile(Path.Join(RepoRoot, shellsJson)).Build();
        var failures = Features(configuration)
            .Where(entry => EfFeatures.ContainsKey(entry.Key))
            .Select(entry => (entry.Key, Provider: string.IsNullOrWhiteSpace(entry["Provider"]) ? EfFeatures[entry.Key] : entry["Provider"]!))
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
    /// Every guard here filters composition entries through <see cref="EfFeatures"/>, so a feature the scan
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
            "These composed EF features resolve no connection here, because the source scan did not find them. " +
            "Either the feature declares Provider/ConnectionName in a shape ProviderSetting() does not match — widen it — " +
            "or it composes onto another feature's context, in which case add it to ContextSharingFeatures:" +
            Environment.NewLine + string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void Every_module_that_deviates_from_the_shared_connection_name_is_mapped_to_its_features()
    {
        var declared = Sources
            .SelectMany(source => DeclaredDefaultConnectionName().Matches(source.Text).Select(match => (source.Path, Value: match.Groups["name"].Value)))
            .Where(declaration => declaration.Value != EfConnectionDefaults.ConnectionName)
            .ToArray();

        var mapped = DeviatingFeatureConnectionNames.Values.ToHashSet(StringComparer.Ordinal);
        var failures = declared
            .Where(declaration => !mapped.Contains(declaration.Value))
            .Select(declaration => $"  {declaration.Path} declares DefaultConnectionName \"{declaration.Value}\", which no feature in DeviatingFeatureConnectionNames claims.")
            .Concat(mapped
                .Where(value => !declared.Any(declaration => declaration.Value == value))
                .Select(value => $"  DeviatingFeatureConnectionNames maps a feature to \"{value}\", which no module under src/ or extensions/ declares."))
            // The keys are what Failure() looks up, so a renamed feature must fail here too, not resolve the shared default in silence.
            .Concat(DeviatingFeatureConnectionNames.Keys
                .Where(feature => !EfFeatures.ContainsKey(feature))
                .Select(feature => $"  DeviatingFeatureConnectionNames is keyed by \"{feature}\", which is not an EF feature the scan found."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            failures.Length == 0,
            "DeviatingFeatureConnectionNames is out of step with the DefaultConnectionName constants in source:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// <see cref="Every_module_that_deviates_from_the_shared_connection_name_is_mapped_to_its_features"/> reads
    /// those constants as text, so a module that computes its default name is invisible to it and would be
    /// resolved here under the shared name it does not actually use. Keeping the declarations to a literal or
    /// the shared constant is what makes that scan sound.
    /// </summary>
    [Fact]
    public void Every_default_connection_name_is_declared_as_a_literal_or_the_shared_constant()
    {
        var computed = Sources
            .SelectMany(source => AnyDefaultConnectionName().Matches(source.Text).Select(match => (source.Path, Expression: match.Groups["expression"].Value.Trim())))
            .Where(declaration => declaration.Expression != $"{nameof(EfConnectionDefaults)}.{nameof(EfConnectionDefaults.ConnectionName)}"
                && !QuotedLiteral().IsMatch(declaration.Expression))
            .Select(declaration => $"  {declaration.Path}: DefaultConnectionName = {declaration.Expression}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            computed.Length == 0,
            $"A module's DefaultConnectionName must be a quoted literal or {nameof(EfConnectionDefaults)}.{nameof(EfConnectionDefaults.ConnectionName)}, " +
            "so the deviation scan can read it without running module code:" +
            Environment.NewLine + string.Join(Environment.NewLine, computed));
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
        var failures =
            (from source in Sources
             let feature = ShellFeatureName().Match(source.Text)
             where feature.Success && EfFeatures.ContainsKey(feature.Groups["name"].Value)
             let expected = ConnectionNameFor(feature.Groups["name"].Value)
             from description in SettingDescription().Matches(source.Text)
             from named in NamedConnectionEntry().Matches(description.Groups["text"].Value)
             where named.Groups["name"].Value != expected
             select $"  {source.Path}: {feature.Groups["name"].Value} names ConnectionStrings:{named.Groups["name"].Value}, but resolves ConnectionStrings:{expected}.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            failures.Length == 0,
            "EF feature settings describe a connection entry their module does not resolve:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static IConfiguration ComposeStack(string composeFile, string shellsJson) =>
        WorkbenchConfiguration()
            .AddJsonFile(Path.Join(RepoRoot, shellsJson))
            .AddJsonFile(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench", "shells.Production.json"))
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
        new ConfigurationBuilder().AddJsonFile(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench", "appsettings.json"));

    private static IEnumerable<(string Name, IConfiguration Configuration)> Compositions()
    {
        yield return ("docker-compose.yml", ComposeStack("docker-compose.yml", "docker/compose/elsa-workbench.shells.json"));
        yield return ("docker-compose.images.yml", ComposeStack("docker-compose.images.yml", "src/Apps/Elsa.Workbench/shells.json"));
        foreach (var shellsJson in new[] { "src/Apps/Elsa.Workbench/shells.json", "src/Apps/Elsa.Workbench/shells.baseline.json" })
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
        var provider = string.IsNullOrWhiteSpace(entry["Provider"]) ? EfFeatures[entry.Key] : entry["Provider"]!;
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
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private static string ConnectionNameFor(string feature) =>
        DeviatingFeatureConnectionNames.GetValueOrDefault(feature, EfConnectionDefaults.ConnectionName);

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

    private sealed record SourceFile(string Path, string Text);

    // The name argument is positional on some features (JintFeature) and named on others.
    [GeneratedRegex("""ShellFeature\(\s*(?:name:\s*)?"(?<name>[^"]+)""")]
    private static partial Regex ShellFeatureName();

    // The initialiser is optional: a feature may declare `public string? Provider { get; set; }` and read the
    // unset value as Sqlite at its own call site.
    [GeneratedRegex("""public\s+string\??\s+Provider\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*"(?<provider>[^"]+)"\s*;|(?!\s*=))""")]
    private static partial Regex ProviderSetting();

    [GeneratedRegex("""const\s+string\s+DefaultConnectionName\s*=\s*"(?<name>[^"]+)"\s*;""")]
    private static partial Regex DeclaredDefaultConnectionName();

    // Anchored on the const declaration: EfModuleBinding takes a DefaultConnectionName record parameter,
    // which is a default for callers rather than a module declaring its own name.
    [GeneratedRegex("""const\s+string\s+DefaultConnectionName\s*=\s*(?<expression>[^;]+);""")]
    private static partial Regex AnyDefaultConnectionName();

    [GeneratedRegex("""^"[^"]*"$""")]
    private static partial Regex QuotedLiteral();

    [GeneratedRegex("""Description\s*=\s*"(?<text>(?:[^"\\]|\\.)*)""")]
    private static partial Regex SettingDescription();

    [GeneratedRegex("""ConnectionStrings:(?<name>[A-Za-z0-9_]+)""")]
    private static partial Regex NamedConnectionEntry();
}
