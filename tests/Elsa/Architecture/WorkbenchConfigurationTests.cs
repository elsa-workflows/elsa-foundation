using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

public sealed class WorkbenchConfigurationTests
{
    [Fact]
    public void Server_environment_variables_override_shell_json_and_command_line_remains_last()
    {
        var program = File.ReadAllText(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench", "Program.cs"));
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
    [InlineData("FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminPassword")]
    [InlineData("GroundworkWorkflowRuntime:RecoveryContinuationSigningKey")]
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
            .SetBasePath(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench"))
            .AddJsonFile("shells.json");
        if (overlay is not null)
            builder.AddJsonFile(overlay);
        return builder.AddInMemoryCollection(environment ?? []).Build();
    }

    // The compose stacks run in Production, where shells.Production.json sits above any shells.json. Only the
    // compose environment can supply what the overlay blanks or requires, so resolve the same layering here.
    [Theory]
    [InlineData("docker-compose.yml", "docker/compose/elsa-workbench.shells.json")]
    [InlineData("docker-compose.images.yml", "src/Apps/Elsa.Workbench/shells.json")]
    public void Production_compose_stacks_supply_the_secrets_the_overlay_requires(string composeFile, string shellsJson)
    {
        var environment = ReadWorkbenchEnvironment(Path.Join(RepoRoot, "docker", "compose", composeFile));
        Assert.Equal("Production", environment["ASPNETCORE_ENVIRONMENT"]);

        var features = new ConfigurationBuilder()
            .AddJsonFile(Path.Join(RepoRoot, shellsJson))
            .AddJsonFile(Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench", "shells.Production.json"))
            .AddInMemoryCollection(environment.Select(x => KeyValuePair.Create(x.Key.Replace("__", ":"), (string?)x.Value)))
            .Build()
            .GetSection("CShells:Shells:default:Features");

        Assert.False(string.IsNullOrWhiteSpace(features["FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminUserName"]));
        Assert.False(string.IsNullOrWhiteSpace(features["FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminPassword"]));

        var signingKey = features["FoundationIdentityOpenIddict:SigningKey"];
        Assert.False(string.IsNullOrWhiteSpace(signingKey));
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(signingKey), out _);

        var recoveryKey = features["GroundworkWorkflowRuntime:RecoveryContinuationSigningKey"] ?? "";
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
