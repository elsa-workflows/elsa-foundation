using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// Resolving the host is the first thing every command does, and the refusals here all happen before a
/// worker is started or a byte is written (FR-011).
/// </summary>
public sealed class HostResolutionTests : IDisposable
{
    private readonly TempDirectory directory = new("elsa-cli-host-");

    public void Dispose() => directory.Dispose();

    [Fact]
    public void A_directory_with_neither_runtime_file_names_both_of_them()
    {
        var refusal = Assert.Throws<CliRefusal>(() => HostLayout.Resolve(directory.Path));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("host-runtime-files-missing", refusal.Code);
        Assert.Contains(refusal.Details, detail => detail.Contains(".runtimeconfig.json", StringComparison.Ordinal));
        Assert.Contains(refusal.Details, detail => detail.Contains(".deps.json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_runtime_config_with_no_deps_file_names_the_deps_file_it_needs()
    {
        File.WriteAllText(directory.File("Contoso.Host.runtimeconfig.json"), "{}");

        var refusal = Assert.Throws<CliRefusal>(() => HostLayout.Resolve(directory.Path));

        Assert.Equal("host-runtime-files-missing", refusal.Code);
        Assert.Equal(["'Contoso.Host.runtimeconfig.json' is present and 'Contoso.Host.deps.json' is missing."], refusal.Details);
    }

    /// <summary>
    /// Two applications in one directory have no tie-break, and picking one would silently script for a
    /// closure the operator did not name.
    /// </summary>
    [Fact]
    public void Two_applications_in_one_directory_are_refused_by_name()
    {
        foreach (var application in new[] { "Contoso.Host", "Fabrikam.Host" })
        {
            File.WriteAllText(directory.File($"{application}.runtimeconfig.json"), "{}");
            File.WriteAllText(directory.File($"{application}.deps.json"), "{}");
        }

        var refusal = Assert.Throws<CliRefusal>(() => HostLayout.Resolve(directory.Path));

        Assert.Equal("host-ambiguous", refusal.Code);
        Assert.Equal(2, refusal.Details.Count);
    }

    [Fact]
    public void A_file_is_not_a_host_directory()
    {
        var file = directory.File("Contoso.Host.dll");
        File.WriteAllText(file, "");

        Assert.Equal("host-not-a-directory", Assert.Throws<CliRefusal>(() => HostLayout.Resolve(file)).Code);
    }

    [Fact]
    public void A_missing_directory_is_named_rather_than_treated_as_empty()
    {
        Assert.Equal("host-missing", Assert.Throws<CliRefusal>(() => HostLayout.Resolve(Path.Join(directory.Path, "nowhere"))).Code);
    }

    [Fact]
    public void A_published_host_resolves_to_its_application_name_and_both_files()
    {
        File.WriteAllText(directory.File("Contoso.Host.runtimeconfig.json"), "{}");
        File.WriteAllText(directory.File("Contoso.Host.deps.json"), "{}");

        var layout = HostLayout.Resolve(directory.Path);

        Assert.Equal("Contoso.Host", layout.Name);
        Assert.Equal(directory.File("Contoso.Host.runtimeconfig.json"), layout.RuntimeConfig);
        Assert.Equal(directory.File("Contoso.Host.deps.json"), layout.DepsFile);
    }

    /// <summary>
    /// The same refusal through the real tool: exit 3, before any worker starts. The unit test above proves
    /// the message; this one proves the process reports it.
    /// </summary>
    [Fact]
    public void The_tool_refuses_a_host_directory_with_no_application()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", directory.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("host-runtime-files-missing", run.Error, StringComparison.Ordinal);
    }
}
