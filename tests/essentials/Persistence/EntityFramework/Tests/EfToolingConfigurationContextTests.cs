using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tooling;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfToolingConfigurationContextTests
{
    [Fact]
    public void Files_are_layered_once_in_runtime_order()
    {
        using var host = new HostFiles();
        host.Write("appsettings.json", "base");
        host.Write("appsettings.Production.json", "app-overlay");
        host.Write("shells.json", "shell-base");
        host.Write("shells.Production.json", "shell-overlay");

        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson);
        Assert.Equal("shell-overlay", context.Configuration["Probe:Value"]);
        Assert.Equal("Production", context.Environment);
        Assert.Equal("default", context.Shell);

        host.Write("shells.Production.json", "changed-after-snapshot");
        Assert.Equal("shell-overlay", context.Configuration["Probe:Value"]);
    }

    [Fact]
    public void Inherited_environment_is_opt_in_and_overrides_files_only_in_that_mode()
    {
        using var host = new HostFiles();
        var key = $"EfToolingProbe{Guid.NewGuid():N}";
        var environmentKey = $"{key}__Value";
        host.Write("shells.Production.json", "file-value", key);
        Environment.SetEnvironmentVariable(environmentKey, "environment-value-canary");
        try
        {
            using var files = host.Create(EfToolingConfigurationContext.WorkbenchJson);
            using var inherited = host.Create(EfToolingConfigurationContext.WorkbenchJsonEnvironment);

            Assert.Equal("file-value", files.Configuration[$"{key}:Value"]);
            Assert.Equal("environment-value-canary", inherited.Configuration[$"{key}:Value"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public void Malformed_source_and_invalid_selector_refuse_without_echoing_values_or_paths()
    {
        using var host = new HostFiles();
        host.WriteRaw("shells.json", "{ connection-value-canary");

        var malformed = Assert.Throws<EfToolingRefusal>(() => host.Create(
            EfToolingConfigurationContext.WorkbenchJson));
        Assert.Equal("configuration-context-invalid", malformed.Code);
        Assert.DoesNotContain("connection-value-canary", malformed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(host.Directory, malformed.ToString(), StringComparison.Ordinal);

        var invalid = Assert.Throws<EfToolingRefusal>(() =>
            host.Create(EfToolingConfigurationContext.WorkbenchJson, shell: null));
        Assert.Equal("configuration-context-invalid", invalid.Code);
    }

    [Fact]
    public void Disposed_context_refuses_reuse_and_disposal_is_idempotent()
    {
        using var host = new HostFiles();
        var context = host.Create(EfToolingConfigurationContext.WorkbenchJson);

        context.Dispose();
        context.Dispose();

        var refusal = Assert.Throws<EfToolingRefusal>(() => _ = context.Configuration);
        Assert.Equal("configuration-context-disposed", refusal.Code);
    }

    [Fact]
    public void Whole_host_probe_accepts_no_shell_and_cancellation_stops_before_source_reads()
    {
        using var host = new HostFiles();
        using var probe = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);
        Assert.Null(probe.Shell);
        Assert.False(probe.ExplicitSelection);

        host.WriteRaw("shells.json", "{ connection-value-canary");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => host.Create(
            EfToolingConfigurationContext.WorkbenchJson, cancellationToken: cancellation.Token));
    }

    private sealed class HostFiles : IDisposable
    {
        public string Directory { get; } = Path.Join(Path.GetTempPath(), $"elsa-tooling-context-{Guid.NewGuid():N}");

        public HostFiles() => System.IO.Directory.CreateDirectory(Directory);

        public EfToolingConfigurationContext Create(
            string source,
            string? shell = "default",
            bool explicitSelection = true,
            CancellationToken cancellationToken = default) =>
            EfToolingConfigurationContext.Create(
                source, Directory, "Host", "Production", shell, explicitSelection, cancellationToken);

        public void Write(string fileName, string value, string key = "Probe") =>
            WriteRaw(fileName, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [key] = new { Value = value }
            }));

        public void WriteRaw(string fileName, string content) =>
            File.WriteAllText(Path.Join(Directory, fileName), content);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
