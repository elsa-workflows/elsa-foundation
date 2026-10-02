using static Elsa.Maps.Generator.ContractTestRepository;

namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the option signals the feature dependency map reports. Pins the
/// sensitive-value signal to the names of a feature's public option properties, so a feature that only mentions a
/// type, a comment or a private member containing "Secret", "Token" or "Key" is not reported as holding sensitive
/// configuration (#2328).
/// </summary>
public static class FeatureFactsContractTests
{
    private const string Sensitive = "sensitive or deployment-specific value signal";

    /// <summary>Each fixture feature's id, its class body, and the option signals the scanner must report for it.</summary>
    private static readonly (string Id, string Body, string[] Signals)[] Cases =
    [
        // The shape that flagged ActivitiesRuntime, ActivitiesBpmn and the keyed-service API features: every probe
        // word appears, and none of them names an option.
        ("MentionsOnly",
            """
            public void ConfigureServices(IServiceCollection services)
            {
                // Keyed by owner. This feature reads no password and no connection string.
                services.TryAddScoped<ActivitySecretInputResolver>();
                services.TryAddKeyedSingleton<BpmnTokenCoordinator>(OwnerId);
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            """,
            []),

        // An option whose type name contains a probe word is still a type name, not a sensitive value.
        ("SecretTypedOption", "public SecretStoreOptions Store { get; set; } = new();", []),

        // A member no shell can configure is not an option.
        ("PrivateMember", "private static readonly string ProcessSigningKey = Guid.NewGuid().ToString();", []),

        ("ConnectionString", "public string? ConnectionString { get; set; }", [Sensitive]),
        ("ApiKey", "public string? ApiKey { get; set; }", [Sensitive]),
        ("ClientSecret", "public string? ClientSecret { get; set; }", [Sensitive]),

        // The other probes keep reading the whole file: a default set inside a method body is still reported.
        ("WholeFileProbe",
            """
            public void ConfigureServices(IServiceCollection services) =>
                services.Configure<SecretStoreOptions>(options => options.Lifetime = TimeSpan.FromMinutes(5));
            """,
            ["code default"])
    ];

    public static void Run() => Use("elsa-feature-facts-tests", root =>
    {
        File.WriteAllText(Path.Join(root, "Elsa.Server.slnx"), "<Solution></Solution>");
        File.WriteAllText(Path.Join(root, "VersionLines.props"),
            "<Project><PropertyGroup><ElsaVersionLineAMembers>;Fixture.LineA;</ElsaVersionLineAMembers></PropertyGroup></Project>");
        WriteProject(root, "src/Fixture.Features", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        foreach (var (id, body, _) in Cases)
            File.WriteAllText(Path.Join(root, $"src/Fixture.Features/{id}Feature.cs"),
                $"[ShellFeature(\"{id}\")]\npublic sealed class {id}Feature : IShellFeature\n{{\n{body}\n}}\n");

        GitInit(root);

        var repo = RepoContext.Discover(root);
        var features = FeatureScanner.Scan(repo, ProjectGraph.Read(repo)).ToDictionary(feature => feature.FeatureId, StringComparer.Ordinal);

        // Every case is checked before failing, so one run names all the features the scanner misreports.
        var failures = Cases
            .Select(testCase => (testCase.Id, Expected: testCase.Signals, Actual: features.GetValueOrDefault(testCase.Id)?.OptionSignals))
            .Where(result => result.Actual is null || !result.Actual.SequenceEqual(result.Expected, StringComparer.Ordinal))
            .Select(result => result.Actual is null
                ? $"{result.Id}: the scanner did not discover the fixture feature."
                : $"{result.Id}: expected option signals [{string.Join(", ", result.Expected)}], got [{string.Join(", ", result.Actual)}].")
            .ToArray();

        if (failures.Length > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));

        Console.WriteLine("Feature facts contract cases passed.");
    });
}
