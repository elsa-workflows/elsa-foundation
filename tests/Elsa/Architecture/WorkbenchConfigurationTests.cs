using Microsoft.Extensions.Configuration;
using Xunit;

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

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
