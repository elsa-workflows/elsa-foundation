using System.Text.Json;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class ResourceAwareLiveCliTests
{
    [Theory]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    public void Resource_target_mismatch_refuses_before_DbContext_or_action_construction_and_database_use(string command)
    {
        using var host = new TempDirectory("elsa-resource-aware-live-host-");
        var sourceHost = DotnetElsa.Host("ResourceAwareLiveHost");
        foreach (var file in Directory.EnumerateFiles(sourceHost, "*", SearchOption.AllDirectories))
        {
            var destination = host.File(Path.GetRelativePath(sourceHost, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        var expectedConnection = $"Data Source={host.File("expected-connection-canary.db")}";
        var suppliedConnection = $"Data Source={host.File("supplied-connection-canary.db")}";
        File.WriteAllText(host.File("appsettings.json"), JsonSerializer.Serialize(new
        {
            ConnectionStrings = new { Expected = expectedConnection },
            Elsa = new
            {
                Persistence = new
                {
                    DefaultResource = "primary",
                    Resources = new
                    {
                        primary = new { Provider = "Sqlite", ConnectionName = "Expected" }
                    }
                }
            }
        }));
        File.WriteAllText(host.File("shells.json"), JsonSerializer.Serialize(new
        {
            CShells = new
            {
                Shells = new
                {
                    @default = new
                    {
                        Name = "default",
                        Features = new { ResourceProbe = new { } }
                    }
                }
            }
        }));

        var contextMarker = host.File("context-constructed.txt");
        var actionMarker = host.File("action-constructed.txt");
        CliRun RunWithConnection(string selectedCommand, string connection) => DotnetElsa.Run(new Dictionary<string, string>
        {
            ["ELSA_RESOURCE_PROBE_CONNECTION"] = connection,
            ["ELSA_RESOURCE_PROBE_CONTEXT_MARKER"] = contextMarker,
            ["ELSA_RESOURCE_PROBE_ACTION_MARKER"] = actionMarker
        },
            "persistence", selectedCommand, "--host", host.Path,
            "--configuration-context", "workbench-json-v1", "--shell", "default", "--resource", "primary",
            "--provider", "Sqlite", "--modules", "Acme.ResourceProbe",
            "--connection-env", "ELSA_RESOURCE_PROBE_CONNECTION");

        var run = RunWithConnection(command, suppliedConnection);
        Assert.Equal(3, run.ExitCode);
        Assert.True(run.Text.Contains("connection-target-mismatch", StringComparison.Ordinal), run.Text);
        Assert.DoesNotContain(expectedConnection, run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(suppliedConnection, run.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(contextMarker), "A mismatched target must be refused before constructing the DbContext.");
        Assert.False(File.Exists(actionMarker), "A mismatched target must be refused before constructing post-migration actions.");
        Assert.False(File.Exists(host.File("supplied-connection-canary.db")), "A mismatched target must not create the supplied SQLite database.");
        Assert.False(File.Exists(host.File("expected-connection-canary.db")), "A mismatched target must not create the configured SQLite database either.");

        var matchingRun = RunWithConnection("validate", expectedConnection);

        Assert.DoesNotContain("connection-target-mismatch", matchingRun.Text, StringComparison.Ordinal);
        Assert.True(matchingRun.ExitCode == 0, matchingRun.Text);
        Assert.True(File.Exists(contextMarker), "A matching target must reach DbContext construction.");
        Assert.True(File.Exists(actionMarker), "A matching target must reach post-migration action construction.");
    }
}
