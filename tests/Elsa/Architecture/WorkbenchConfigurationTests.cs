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

    [Fact]
    public void Production_shell_overlay_clears_the_committed_development_admin_password()
    {
        var workbenchDirectory = Path.Join(RepoRoot, "src", "Apps", "Elsa.Workbench");
        const string passwordPath =
            "CShells:Shells:default:Features:FoundationIdentityAspNetCoreIdentityGroundwork:SeedAdminPassword";

        var productionConfiguration = new ConfigurationBuilder()
            .SetBasePath(workbenchDirectory)
            .AddJsonFile("shells.json")
            .AddJsonFile("shells.Production.json")
            .Build();

        Assert.True(string.IsNullOrEmpty(productionConfiguration[passwordPath]));

        var environmentOverride = new ConfigurationBuilder()
            .SetBasePath(workbenchDirectory)
            .AddJsonFile("shells.json")
            .AddJsonFile("shells.Production.json")
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [passwordPath] = "environment-override"
            })
            .Build();

        Assert.Equal("environment-override", environmentOverride[passwordPath]);
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
