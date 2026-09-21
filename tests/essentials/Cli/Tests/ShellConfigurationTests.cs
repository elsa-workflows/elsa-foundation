using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// How the tool reads a host's shell configuration (spec 171 FR-035, FR-038). Every case here is one where
/// reading the file wrongly would look like success: a shape the reader does not understand presents as
/// "this shell enables nothing", and a check that compared nothing would still report itself as run.
/// </summary>
public sealed class ShellConfigurationTests : IDisposable
{
    private readonly TempDirectory host = new("elsa-cli-shells-");

    public ShellConfigurationTests()
    {
        // The two files HostLayout requires. Nothing here reaches the worker: shell configuration is read
        // by the front end, before a worker is ever launched.
        File.WriteAllText(host.File("Contoso.Host.runtimeconfig.json"), "{}");
        File.WriteAllText(host.File("Contoso.Host.deps.json"), "{}");
    }

    public void Dispose() => host.Dispose();

    [Fact]
    public void A_features_section_mixing_array_and_object_map_children_is_refused()
    {
        var run = List("""{"CShells":{"Shells":{"default":{"Features":{"0":"WorkflowsRuntimeEntityFrameworkCore","WorkflowsRuntimeEntityFrameworkCore":{}}}}}}""");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("shells-configuration-invalid", run.Text, StringComparison.Ordinal);
        Assert.Contains("mixes array and object-map children", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_feature_whose_value_is_neither_a_boolean_nor_an_object_is_refused()
    {
        var run = List("""{"CShells":{"Shells":{"default":{"Features":{"WorkflowsRuntimeEntityFrameworkCore":"PostgreSql"}}}}}""");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("must be true, false, a 'true'/'false' string, or an object", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_array_entry_with_no_name_is_refused()
    {
        var run = List("""{"CShells":{"Shells":{"default":{"Features":[{"Provider":"PostgreSql"}]}}}}""");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("must define a non-empty 'Name' property", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_shells_file_is_refused_without_quoting_its_contents()
    {
        var run = List("""{"CShells": {"Shells": """);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("shells-configuration-unreadable", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("CShells", run.Text, StringComparison.Ordinal);
    }

    private CliRun List(string shells)
    {
        File.WriteAllText(host.File("shells.json"), shells);
        return DotnetElsa.Run("persistence", "list", "--host", host.Path, "--from-host", "--shell", "default");
    }
}
