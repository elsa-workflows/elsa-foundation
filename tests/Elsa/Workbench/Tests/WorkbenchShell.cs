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

    public static readonly WorkbenchShell Development = new("Development", "shells.json", null, new Dictionary<string, string>());

    public static readonly WorkbenchShell Baseline = new("Development", "shells.baseline.json", null, new Dictionary<string, string>());

    /// <summary>
    /// <c>shells.Production.json</c> blanks the demo secrets committed in <c>shells.json</c>, so a Production host must be
    /// given its own: the durable-recovery HMAC key and the initial admin's password (activation fails without either),
    /// and the OpenIddict token signing key (without it the shell reports ready but every request through
    /// authentication fails).
    /// </summary>
    public static readonly WorkbenchShell Production = new("Production", "shells.json", "shells.Production.json", new Dictionary<string, string>
    {
        [$"{FeaturesPath}:GroundworkWorkflowRuntime:RecoveryContinuationSigningKey"] = "smoke-test-recovery-continuation-signing-key",
        [$"{FeaturesPath}:FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminPassword"] = $"Smoke-{Guid.NewGuid():n}!",
        [$"{FeaturesPath}:FoundationIdentityOpenIddict:SigningKey"] = NewPkcs8SigningKey()
    });

    public static readonly IReadOnlyDictionary<string, WorkbenchShell> All = new[] { Development, Baseline, Production }
        .ToDictionary(shell => shell.Name, StringComparer.Ordinal);

    public string Name => EnvironmentOverlay is null ? ShellFile : $"{ShellFile} + {EnvironmentOverlay}";

    /// <summary>The feature names the committed shell file and overlay list for the default shell.</summary>
    public IReadOnlySet<string> ListedFeatures() =>
        new[] { ShellFile, EnvironmentOverlay }
            .OfType<string>()
            .SelectMany(file => FeatureSection(file).Select(feature => feature.Key))
            .ToHashSet(StringComparer.Ordinal);

    private static string NewPkcs8SigningKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
    }

    private static JsonObject FeatureSection(string file)
    {
        var node = JsonNode.Parse(File.ReadAllText(WorkbenchBuild.SourceFile(file)));
        foreach (var segment in FeaturesPath.Split(':'))
            node = node?[segment];

        return node?.AsObject() ?? throw new InvalidOperationException($"{file} has no {FeaturesPath} section.");
    }
}
