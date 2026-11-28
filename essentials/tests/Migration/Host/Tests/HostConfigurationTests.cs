using Elsa.Migration.Host.Tests.Support;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Migration.Host.Tests;

/// <summary>
/// Spec 1237 T004a: <c>MigrationHost.CreateBuilder</c> calls <c>AddHostConfiguration</c> on top of
/// <c>WebApplication.CreateBuilder</c>'s default sources, so the host keys of the prefixed sources survive, and the layers
/// keep spec 1236 R-3's order. These build no app.
/// </summary>
[Collection(nameof(HostEnvironmentCollection))]
public sealed class HostConfigurationTests
{
    [Fact]
    public void ASPNETCORE_URLS_still_reaches_the_configuration_as_urls()
    {
        var root = InProcessRun.NewRoot();
        var previous = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:5196");
        try
        {
            var builder = MigrationHost.CreateBuilder(["--environment=Development", $"--contentRoot={InProcessRun.CommittedContentRoot(root)}"]);

            Assert.Equal("http://localhost:5196", builder.Configuration["urls"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", previous);
            InProcessRun.DeleteRoot(root);
        }
    }

    [Fact]
    public void Without_local_sources_only_the_committed_files_are_read_from_the_host_folder()
    {
        var root = InProcessRun.NewRoot();
        var folder = InProcessRun.CommittedContentRoot(root);
        File.WriteAllText(Path.Combine(folder, "shells.Local.json"), """{ "Probe": "local" }""");
        File.WriteAllText(Path.Combine(folder, "appsettings.json"), """{ "Probe": "appsettings", "Order": "appsettings" }""");
        File.WriteAllText(Path.Combine(folder, "shells.Development.json"), """{ "Order": "shells.Development" }""");
        try
        {
            var committed = new ConfigurationBuilder();
            MigrationHost.AddHostConfiguration(committed, folder, "Development", typeof(MigrationHost).Assembly, ["--Probe=args"], includeLocalSources: false);
            var local = new ConfigurationBuilder();
            MigrationHost.AddHostConfiguration(local, folder, "Development", typeof(MigrationHost).Assembly, ["--Probe=args"], includeLocalSources: true);

            Assert.Equal("appsettings", committed.Build()["Probe"]);
            Assert.Equal("shells.Development", committed.Build()["Order"]);
            Assert.Equal("args", local.Build()["Probe"]);
        }
        finally
        {
            InProcessRun.DeleteRoot(root);
        }
    }
}

/// <summary>Tests that change process environment variables run one at a time.</summary>
[CollectionDefinition(nameof(HostEnvironmentCollection), DisableParallelization = true)]
public sealed class HostEnvironmentCollection;
