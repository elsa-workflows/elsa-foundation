using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ElsaServerConfigurationTests
{
    [Fact]
    public void Server_environment_variables_override_shell_json_and_command_line_remains_last()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", "Program.cs"));
        var shellDefaults = program.IndexOf("AddJsonFile(\"shells.json\"", StringComparison.Ordinal);
        var shellEnvironmentOverlay = program.IndexOf("AddJsonFile($\"shells.{builder.Environment.EnvironmentName}.json\"", StringComparison.Ordinal);
        var environmentVariables = program.IndexOf(".AddEnvironmentVariables()", shellEnvironmentOverlay, StringComparison.Ordinal);
        var commandLine = program.IndexOf(".AddCommandLine(args)", environmentVariables, StringComparison.Ordinal);

        Assert.True(shellDefaults >= 0, "Elsa.Server must load shells.json.");
        Assert.True(shellEnvironmentOverlay > shellDefaults, "The environment-specific shell overlay must follow shells.json.");
        Assert.True(environmentVariables > shellEnvironmentOverlay, "Environment variables must be re-added after shell configuration.");
        Assert.True(commandLine > environmentVariables, "Command-line arguments must retain precedence over environment variables.");
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
