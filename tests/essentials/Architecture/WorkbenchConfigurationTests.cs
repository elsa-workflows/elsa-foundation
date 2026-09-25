using CShells;
using CShells.Lifecycle.Blueprints;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

public sealed class WorkbenchConfigurationTests
{
    [Fact]
    public async Task Pinned_cshells_package_merges_object_map_settings_and_disabled_state()
    {
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":false,"Limit":0,"Optional":null,"Empty":"","EmptyObject":{},"EmptyArray":[]},"B":false}}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":true}}}}}}
            """;

        var shell = await ComposePinnedShellAsync(baseJson, overlayJson);

        Assert.Contains("A", shell.EnabledFeatures);
        Assert.Contains("B", shell.DisabledFeatures);
        Assert.Equal("True", shell.ConfigurationData["A:Flag"]);
        Assert.Equal("0", shell.ConfigurationData["A:Limit"]);
        Assert.Equal("", shell.ConfigurationData["A:Empty"]);
        Assert.DoesNotContain("A:Optional", shell.ConfigurationData.Keys);
        Assert.DoesNotContain("A:EmptyObject", shell.ConfigurationData.Keys);
        Assert.Equal("", shell.ConfigurationData["A:EmptyArray"]);

        using var raw = JsonDocument.Parse(baseJson);
        var feature = raw.RootElement.GetProperty("CShells").GetProperty("Shells")
            .GetProperty("default").GetProperty("Features").GetProperty("A");
        Assert.Equal(JsonValueKind.False, feature.GetProperty("Flag").ValueKind);
        Assert.Equal(JsonValueKind.Number, feature.GetProperty("Limit").ValueKind);
        Assert.Equal(JsonValueKind.Null, feature.GetProperty("Optional").ValueKind);
        Assert.Equal(JsonValueKind.String, feature.GetProperty("Empty").ValueKind);
        Assert.Equal(JsonValueKind.Object, feature.GetProperty("EmptyObject").ValueKind);
        Assert.Equal(JsonValueKind.Array, feature.GetProperty("EmptyArray").ValueKind);
    }

    [Fact]
    public async Task Pinned_cshells_package_scalar_object_overlays_retain_base_scalar_values()
    {
        const string disabledBase = """
            {"CShells":{"Shells":{"default":{"Features":{"A":false}}}}}
            """;
        const string objectOverlay = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":true}}}}}}
            """;
        const string settingsBase = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{"Flag":false}}}}}}
            """;
        const string resetOverlay = """
            {"CShells":{"Shells":{"default":{"Features":{"A":true}}}}}
            """;

        var stillDisabled = await ComposePinnedShellAsync(disabledBase, objectOverlay);
        var reset = await ComposePinnedShellAsync(settingsBase, resetOverlay);

        Assert.Contains("A", stillDisabled.DisabledFeatures);
        Assert.DoesNotContain("A:Flag", stillDisabled.ConfigurationData.Keys);
        Assert.Contains("A", reset.EnabledFeatures);
        Assert.Contains("A", reset.FeatureSettingResets);
        Assert.DoesNotContain("A:Flag", reset.ConfigurationData.Keys);
    }

    [Fact]
    public async Task Pinned_cshells_package_treats_array_enabled_field_as_setting()
    {
        const string json = """
            {"CShells":{"Shells":{"default":{"Features":["B",{"Name":"A","Enabled":false,"Limit":0}]}}}}
            """;

        var shell = await ComposePinnedShellAsync(json);

        Assert.Contains("A", shell.EnabledFeatures);
        Assert.Contains("B", shell.EnabledFeatures);
        Assert.Empty(shell.DisabledFeatures);
        Assert.Equal("False", shell.ConfigurationData["A:Enabled"]);
        Assert.Equal("0", shell.ConfigurationData["A:Limit"]);
    }

    [Fact]
    public async Task Pinned_cshells_package_merges_array_overlays_by_index()
    {
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":["A","B"]}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":["C"]}}}}
            """;

        var shell = await ComposePinnedShellAsync(baseJson, overlayJson);

        Assert.Equal(["C", "B"], shell.EnabledFeatures);
    }

    [Fact]
    public async Task Pinned_cshells_package_rejects_cross_shape_overlay()
    {
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":true}}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":["B"]}}}}
            """;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ComposePinnedShellAsync(baseJson, overlayJson));

        Assert.Contains("ambiguous 'Features' section", error.Message);
    }

    [Fact]
    public async Task Pinned_cshells_package_rejects_case_equivalent_array_feature_ids()
    {
        const string json = """
            {"CShells":{"Shells":{"default":{"Features":["A","a"]}}}}
            """;
        const string baseJson = """
            {"CShells":{"Shells":{"default":{"Features":["A","B"]}}}}
            """;
        const string overlayJson = """
            {"CShells":{"Shells":{"default":{"Features":["b"]}}}}
            """;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ComposePinnedShellAsync(json));
        var layeredError = await Assert.ThrowsAsync<InvalidOperationException>(() => ComposePinnedShellAsync(baseJson, overlayJson));

        Assert.Contains("duplicate configured feature name", error.Message);
        Assert.Contains("duplicate configured feature name", layeredError.Message);
    }

    [Fact]
    public async Task Pinned_cshells_package_collapses_null_and_empty_feature_objects()
    {
        const string nullJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":null}}}}}
            """;
        const string emptyJson = """
            {"CShells":{"Shells":{"default":{"Features":{"A":{}}}}}}
            """;

        var nullShell = await ComposePinnedShellAsync(nullJson);
        var emptyShell = await ComposePinnedShellAsync(emptyJson);

        Assert.Equal(nullShell.EnabledFeatures, emptyShell.EnabledFeatures);
        Assert.Equal(nullShell.ConfigurationData, emptyShell.ConfigurationData);
    }

    private static async Task<ShellSettings> ComposePinnedShellAsync(string baseJson, string? overlayJson = null)
    {
        using var baseStream = new MemoryStream(Encoding.UTF8.GetBytes(baseJson));
        var builder = new ConfigurationBuilder().AddJsonStream(baseStream);
        using var overlayStream = overlayJson is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(overlayJson));
        if (overlayStream is not null)
            builder.AddJsonStream(overlayStream);

        var section = builder.Build().GetSection("CShells:Shells:default");
        return await new ConfigurationShellBlueprint("default", section).ComposeAsync();
    }

    [Fact]
    public void Server_environment_variables_override_shell_json_and_command_line_remains_last()
    {
        var program = File.ReadAllText(Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench", "Program.cs"));
        var shellDefaults = program.IndexOf("AddJsonFile(\"shells.json\"", StringComparison.Ordinal);
        var shellEnvironmentOverlay = program.IndexOf("AddJsonFile($\"shells.{builder.Environment.EnvironmentName}.json\"", StringComparison.Ordinal);
        var environmentVariables = program.IndexOf(".AddEnvironmentVariables()", shellEnvironmentOverlay, StringComparison.Ordinal);
        var commandLine = program.IndexOf(".AddCommandLine(args)", environmentVariables, StringComparison.Ordinal);

        Assert.True(shellDefaults >= 0, "Elsa.Workbench must load shells.json.");
        Assert.True(shellEnvironmentOverlay > shellDefaults, "The environment-specific shell overlay must follow shells.json.");
        Assert.True(environmentVariables > shellEnvironmentOverlay, "Environment variables must be re-added after shell configuration.");
        Assert.True(commandLine > environmentVariables, "Command-line arguments must retain precedence over environment variables.");
    }

    [Theory]
    [InlineData("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword")]
    [InlineData("WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey")]
    [InlineData("WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey")]
    public void Production_shell_overlay_clears_a_committed_development_secret(string featureSetting)
    {
        var path = $"CShells:Shells:default:Features:{featureSetting}";

        Assert.False(string.IsNullOrEmpty(BuildShellConfiguration()[path]), "shells.json must carry the development value.");
        Assert.True(string.IsNullOrEmpty(BuildShellConfiguration("shells.Production.json")[path]));
        Assert.Equal(
            "environment-override",
            BuildShellConfiguration("shells.Production.json", new() { [path] = "environment-override" })[path]);
    }

    private static IConfiguration BuildShellConfiguration(
        string? overlay = null,
        Dictionary<string, string?>? environment = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench"))
            .AddJsonFile("shells.json");
        if (overlay is not null)
            builder.AddJsonFile(overlay);
        return builder.AddInMemoryCollection(environment ?? []).Build();
    }

    // The compose stacks run in Production, where shells.Production.json sits above any shells.json. Only the
    // compose environment can supply what the overlay blanks or requires, so resolve the same layering here.
    [Theory]
    [InlineData("docker-compose.yml", "docker/compose/elsa-workbench.shells.json")]
    [InlineData("docker-compose.images.yml", "src/apps/Elsa.Workbench/shells.json")]
    public void Production_compose_stacks_supply_the_secrets_the_overlay_requires(string composeFile, string shellsJson)
    {
        var environment = ReadWorkbenchEnvironment(Path.Join(RepoRoot, "docker", "compose", composeFile));
        Assert.Equal("Production", environment["ASPNETCORE_ENVIRONMENT"]);

        var features = new ConfigurationBuilder()
            .AddJsonFile(Path.Join(RepoRoot, shellsJson))
            .AddJsonFile(Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench", "shells.Production.json"))
            .AddInMemoryCollection(environment.Select(x => KeyValuePair.Create(x.Key.Replace("__", ":"), (string?)x.Value)))
            .Build()
            .GetSection("CShells:Shells:default:Features");

        Assert.False(string.IsNullOrWhiteSpace(features["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminUserName"]));
        Assert.False(string.IsNullOrWhiteSpace(features["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword"]));

        var signingKey = features["FoundationIdentityOpenIddict:SigningKey"];
        Assert.False(string.IsNullOrWhiteSpace(signingKey));
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(signingKey), out _);
        Assert.True(rsa.KeySize >= 2048, $"The OpenIddict signing key is {rsa.KeySize} bits; shell activation requires at least 2048.");

        var recoveryKey = features["WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] ?? "";
        Assert.True(Encoding.UTF8.GetByteCount(recoveryKey) >= 32, "The recovery continuation signing key needs at least 32 UTF-8 bytes.");
    }

    private static Dictionary<string, string> ReadWorkbenchEnvironment(string composePath)
    {
        using var reader = File.OpenText(composePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var environment = Assert.IsType<YamlMappingNode>(yaml.Documents[0].RootNode["services"]["elsa-workbench"]["environment"]);
        return environment.Children.ToDictionary(
            x => Assert.IsType<YamlScalarNode>(x.Key).Value!,
            x => Assert.IsType<YamlScalarNode>(x.Value).Value!);
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
