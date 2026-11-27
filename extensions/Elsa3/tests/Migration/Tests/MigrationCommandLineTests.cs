using Elsa3.Migration.Cli;

namespace Elsa3.Migration.Tests;

/// <summary>
/// Spec 1237 T011: the command line of contracts/cli.md. Argument refusals exit 2 with one refusal line on standard error
/// and nothing on standard output; they need no composition.
/// </summary>
public sealed class MigrationCommandLineTests
{
    [Theory]
    [InlineData]
    [InlineData("workflows")]
    [InlineData("workflows", "migrate", "export")]
    [InlineData("workflows", "inventory")]
    [InlineData("workflows", "inventory", "export", "--unknown")]
    [InlineData("workflows", "inventory", "export", "--out")]
    [InlineData("workflows", "assess", "export", "extra")]
    public async Task Invalid_command_lines_refuse_with_the_arguments_reason(params string[] args)
    {
        var (exitCode, output, error) = await RunAsync(args);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output);
        var line = Assert.Single(error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("elsa3-migration: refused: arguments: ", line);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("assess")]
    public async Task A_missing_export_refuses_with_input_missing_before_anything_is_composed(string command)
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa3-migration-cli-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "out");

        var (exitCode, standardOutput, error) = await RunAsync(["workflows", command, Path.Combine(root, "missing"), "--out", output]);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOutput);
        Assert.StartsWith("elsa3-migration: refused: input-missing: ", error.Trim());
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task An_export_without_definitions_refuses_with_input_empty_and_leaves_a_missing_out_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa3-migration-cli-" + Guid.NewGuid().ToString("N"));
        var export = Path.Combine(root, "export");
        Directory.CreateDirectory(export);
        File.WriteAllText(Path.Combine(export, "settings.json"), """{ "theme": "dark" }""");
        var output = Path.Combine(root, "out");
        try
        {
            var (exitCode, _, error) = await RunAsync(["workflows", "inventory", export, "--out", output]);

            Assert.Equal(2, exitCode);
            Assert.StartsWith("elsa3-migration: refused: input-empty: ", error.Trim());
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Help_prints_and_exits_0()
    {
        var (exitCode, output, error) = await RunAsync(["workflows", "inventory", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("--all-versions", output);
        Assert.Equal(string.Empty, error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await MigrationCommandLine.RunAsync(args, output, error, CliHostWiring.Create(), CancellationToken.None);
        return (exitCode, output.ToString(), error.ToString());
    }
}
