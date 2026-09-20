using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Elsa.Workbench.Tests;

/// <summary>
/// A stock Workbench composition as an operator deploys it: the hosting environment, the committed shell file
/// (copied in as <c>shells.json</c>), an optional environment overlay (copied in as <c>shells.{Environment}.json</c>),
/// and the settings the operator must supply on top because the committed files deliberately leave them blank.
/// </summary>
public sealed record WorkbenchShell(
    string Environment,
    string ShellFile,
    string? EnvironmentOverlay,
    IReadOnlyDictionary<string, string> Settings)
{
    private const string FeaturesPath = "CShells:Shells:default:Features";

    public static readonly WorkbenchShell Development = Create("Development", "shells.json", null);

    public static readonly WorkbenchShell Baseline = Create("Development", "shells.baseline.json", null);

    /// <summary>
    /// <c>shells.Production.json</c> blanks the demo secrets committed in <c>shells.json</c> and turns off OpenIddict's
    /// ephemeral development keys, so a Production host must be given its own durable-recovery HMAC key, initial admin
    /// password, and OpenIddict token signing key. Activation fails without any one of them.
    /// </summary>
    public static readonly WorkbenchShell Production = Create("Production", "shells.json", "shells.Production.json", new Dictionary<string, string>
    {
        // appsettings.Production.json validates instead of migrating, because a deployment applies migrations from
        // CI/CD before the host runs. This test starts against an empty database with no such step, which is the
        // case that policy is built to refuse, so it migrates on activation like the other compositions.
        ["Elsa:Persistence:EntityFramework:Migrate:Policy"] = "AutoMigrate",
        [$"{FeaturesPath}:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = "smoke-test-recovery-continuation-signing-key",
        [$"{FeaturesPath}:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = "smoke-test-hierarchy-cursor-signing-key",
        [$"{FeaturesPath}:FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword"] = $"Smoke-{Guid.NewGuid():n}!",
        [$"{FeaturesPath}:FoundationIdentityOpenIddict:SigningKey"] = Convert.ToBase64String(RSA.Create(2048).ExportPkcs8PrivateKey())
    });

    /// <summary>
    /// A composition with its provider pinned to SQLite, on top of the settings an operator must supply.
    /// </summary>
    /// <remarks>
    /// The committed shell files select PostgreSql, which no test host runs. These tests assert that a composition
    /// activates and serves requests, not that a provider binds: <c>ModuleMigrationTests</c> covers every module
    /// against all four providers. SQLite keeps them container-free, and a relative database file resolves against
    /// each run's own working directory, which is what isolates two hosts started in parallel.
    /// </remarks>
    private static WorkbenchShell Create(
        string environment,
        string shellFile,
        string? environmentOverlay,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var pinned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Elsa"] = "Data Source=elsa.db"
        };

        // Every EF feature binds its own provider, except the dashboard's reader: it has no provider setting and
        // queries the Runtime and Design contexts.
        foreach (var feature in ListedFeatures(shellFile, environmentOverlay)
                     .Where(feature => feature.Contains("EntityFrameworkCore", StringComparison.Ordinal))
                     .Where(feature => feature != "WorkflowsDashboardEntityFrameworkCore"))
            pinned[$"{FeaturesPath}:{feature}:Provider"] = "Sqlite";

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            pinned[key] = value;

        return new WorkbenchShell(environment, shellFile, environmentOverlay, pinned);
    }

    public static readonly IReadOnlyDictionary<string, WorkbenchShell> All = new[] { Development, Baseline, Production }
        .ToDictionary(shell => shell.Name, StringComparer.Ordinal);

    public string Name => EnvironmentOverlay is null ? ShellFile : $"{ShellFile} + {EnvironmentOverlay}";

    /// <summary>This composition with one operator-supplied default-shell feature setting left out.</summary>
    public WorkbenchShell Without(string featureSetting)
    {
        var key = $"{FeaturesPath}:{featureSetting}";
        return Settings.ContainsKey(key)
            ? this with { Settings = Settings.Where(setting => setting.Key != key).ToDictionary() }
            : throw new ArgumentException($"{Name} does not supply {featureSetting}.", nameof(featureSetting));
    }

    /// <summary>The feature names the committed shell file and overlay list for the default shell.</summary>
    public IReadOnlySet<string> ListedFeatures() => ListedFeatures(ShellFile, EnvironmentOverlay);

    private static IReadOnlySet<string> ListedFeatures(string shellFile, string? environmentOverlay) =>
        new[] { shellFile, environmentOverlay }
            .OfType<string>()
            .SelectMany(file => FeatureSection(file).Select(feature => feature.Key))
            .ToHashSet(StringComparer.Ordinal);

    private static JsonObject FeatureSection(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(WorkbenchBuild.SourceFile(file)));
        foreach (var segment in FeaturesPath.Split(':'))
            node = node?[segment];

        return node?.AsObject() ?? throw new InvalidOperationException($"{file} has no {FeaturesPath} section.");
    }
}
