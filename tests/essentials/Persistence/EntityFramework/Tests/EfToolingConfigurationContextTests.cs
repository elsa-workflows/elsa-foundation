using System.Text;
using System.Text.Json;
using System.Reflection;
using CShells.Configuration;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.Configuration;
using Xunit;

[assembly: EfToolingShellDefaults(typeof(Elsa.Persistence.EntityFramework.Tests.ToolingContextTestDefaults))]

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfToolingConfigurationContextTests
{
    private static readonly Assembly[] RuntimeFeatureAssemblies =
    [
        typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsRuntimeResumptionFeature).Assembly,
        typeof(TasksFeature).Assembly
    ];

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

    [Fact]
    public void Expected_connection_requires_an_independent_exact_live_match()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """{"ConnectionStrings":{"Shared":"connection-value-canary","Empty":" "}}""");
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson);

        context.VerifyExpectedConnection("Shared", "connection-value-canary");

        var missingExpected = Assert.Throws<EfToolingRefusal>(() =>
            context.VerifyExpectedConnection("Other", "connection-value-canary"));
        var emptyExpected = Assert.Throws<EfToolingRefusal>(() =>
            context.VerifyExpectedConnection("Empty", "connection-value-canary"));
        var missingActual = Assert.Throws<EfToolingRefusal>(() =>
            context.VerifyExpectedConnection("Shared", null));
        var emptyActual = Assert.Throws<EfToolingRefusal>(() =>
            context.VerifyExpectedConnection("Shared", " "));
        var mismatch = Assert.Throws<EfToolingRefusal>(() =>
            context.VerifyExpectedConnection("Shared", "Connection-value-canary"));

        Assert.Equal("expected-connection-unresolved", missingExpected.Code);
        Assert.Equal("expected-connection-unresolved", emptyExpected.Code);
        Assert.Equal("invalid-request", missingActual.Code);
        Assert.Equal("invalid-request", emptyActual.Code);
        Assert.Equal("connection-target-mismatch", mismatch.Code);
        foreach (var refusal in new[] { missingExpected, emptyExpected, missingActual, emptyActual, mismatch })
            Assert.DoesNotContain("connection-value-canary", refusal.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expected_connection_uses_only_the_selected_frozen_source_mode()
    {
        using var host = new HostFiles();
        var reference = $"Shared{Guid.NewGuid():N}";
        var environmentKey = $"ConnectionStrings__{reference}";
        host.WriteRaw("appsettings.json", JsonSerializer.Serialize(new
        {
            ConnectionStrings = new Dictionary<string, string> { [reference] = "file-target-canary" }
        }));
        Environment.SetEnvironmentVariable(environmentKey, "environment-target-canary");
        try
        {
            using var files = host.Create(EfToolingConfigurationContext.WorkbenchJson);
            using var inherited = host.Create(EfToolingConfigurationContext.WorkbenchJsonEnvironment);
            Environment.SetEnvironmentVariable(environmentKey, "changed-after-snapshot");

            files.VerifyExpectedConnection(reference, "file-target-canary");
            inherited.VerifyExpectedConnection(reference, "environment-target-canary");
            var mismatch = Assert.Throws<EfToolingRefusal>(() =>
                files.VerifyExpectedConnection(reference, "environment-target-canary"));
            Assert.Equal("connection-target-mismatch", mismatch.Code);
            Assert.DoesNotContain("target-canary", mismatch.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public void Closed_descriptor_selects_one_host_owned_snapshot()
    {
        using var host = new HostFiles();
        host.Write("shells.Production.json", "selected-value");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(host.Request()));
        using var context = EfToolingHost.CreateConfigurationContext(stream, CancellationToken.None);

        Assert.Equal(EfToolingConfigurationContext.WorkbenchJson, context.Source);
        Assert.Equal(host.Directory, context.HostDirectory);
        Assert.Equal("selected-value", context.Configuration["Probe:Value"]);
        host.Write("shells.Production.json", "changed-after-factory");
        Assert.Equal("selected-value", context.Configuration["Probe:Value"]);
    }

    [Fact]
    public void Closed_descriptor_refuses_unknown_duplicate_and_unsupported_fields_without_echoing_input()
    {
        using var host = new HostFiles();
        var valid = host.Request();
        var malformed = new[]
        {
            valid[..^1] + ",\"connection-value-canary\":\"secret-canary\"}",
            valid[..^1] + ",\"source\":\"workbench-json-environment-v1\"}",
            valid.Replace("\"contextVersion\":1", "\"contextVersion\":2", StringComparison.Ordinal),
            valid.Replace("\"explicitSelection\":true", "\"otherSelection\":true", StringComparison.Ordinal),
            "[\"not-an-object\"]"
        };

        foreach (var request in malformed)
        {
            var refusal = Assert.Throws<EfToolingRefusal>(() => Parse(request));
            Assert.Equal("configuration-context-invalid", refusal.Code);
            Assert.DoesNotContain("canary", refusal.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(host.Directory, refusal.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Selected_host_assembly_uses_the_same_declared_composer_as_runtime()
    {
        var hostAssembly = typeof(EfToolingConfigurationContextTests).Assembly;
        var hostDirectory = Path.GetDirectoryName(hostAssembly.Location)!;
        using var context = EfToolingConfigurationContext.Create(
            EfToolingConfigurationContext.WorkbenchJson, hostDirectory, hostAssembly.GetName().Name!,
            "Production", "default", explicitSelection: true, CancellationToken.None);

        Assert.IsType<ToolingContextTestDefaults>(context.CreateHostDefaults(hostAssembly));

        using var mismatched = EfToolingConfigurationContext.Create(
            EfToolingConfigurationContext.WorkbenchJson, hostDirectory, "DifferentHost",
            "Production", "default", explicitSelection: true, CancellationToken.None);
        Assert.Equal("configuration-context-invalid",
            Assert.Throws<EfToolingRefusal>(() => mismatched.CreateHostDefaults(hostAssembly)).Code);
    }

    [Fact]
    public void Composer_free_host_allows_probe_but_refuses_explicit_selection()
    {
        var hostAssembly = typeof(EfToolingConfigurationContext).Assembly;
        var hostDirectory = Path.GetDirectoryName(hostAssembly.Location)!;
        var hostName = hostAssembly.GetName().Name!;
        using var probe = EfToolingConfigurationContext.Create(
            EfToolingConfigurationContext.WorkbenchJson, hostDirectory, hostName,
            "Production", null, explicitSelection: false, CancellationToken.None);
        using var selected = EfToolingConfigurationContext.Create(
            EfToolingConfigurationContext.WorkbenchJson, hostDirectory, hostName,
            "Production", "default", explicitSelection: true, CancellationToken.None);

        Assert.Null(probe.CreateHostDefaults(hostAssembly));
        Assert.Equal("host-not-enrolled",
            Assert.Throws<EfToolingRefusal>(() => selected.CreateHostDefaults(hostAssembly)).Code);
    }

    [Fact]
    public void Tooling_composes_the_dependency_expanded_shell_without_expected_value_lookup()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """
            {
              "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } },
              "CShells": { "Shells": { "default": {
                "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } }
            }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson);
        Assert.Equal("Sqlite", context.Configuration["Elsa:Persistence:Resources:primary:Provider"]);
        Assert.Equal("Shared", context.Configuration["Elsa:Persistence:Resources:primary:ConnectionName"]);

        var result = context.PrepareShell(new ToolingContextTestDefaults(),
            RuntimeFeatureAssemblies, CancellationToken.None);

        Assert.True(result.HasApplicableResource);
        Assert.Empty(result.RefusalCodes);
        Assert.Contains("expected-connection-unchecked", result.UnresolvedCodes);
        Assert.Equal("Sqlite", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:Provider"]);
        Assert.Equal("Shared", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:ConnectionName"]);
        Assert.Equal("primary", Assert.Single(result.Participants).ResourceName);
    }

    [Fact]
    public void Tooling_sees_code_only_feature_and_default_resource_selection()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """
            { "Elsa": { "Persistence": {
              "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } }
            } } }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson);

        var result = context.PrepareShell(new ToolingCodeDefaultTestDefaults(),
            RuntimeFeatureAssemblies, CancellationToken.None);

        Assert.True(result.HasApplicableResource);
        Assert.Empty(result.RefusalCodes);
        Assert.Equal("primary", Assert.Single(result.Participants).ResourceName);
        Assert.Contains("expected-connection-unchecked", result.UnresolvedCodes);
    }

    private static EfToolingConfigurationContext Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return EfToolingConfigurationContext.CreateFromRequest(stream, CancellationToken.None);
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

        public string Request() => JsonSerializer.Serialize(new
        {
            contextVersion = EfToolingConfigurationContext.Version,
            source = EfToolingConfigurationContext.WorkbenchJson,
            hostDirectory = Directory,
            hostName = "Host",
            environment = "Production",
            shell = "default",
            explicitSelection = true
        });

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

public sealed class ToolingContextTestDefaults : IEfToolingShellDefaults
{
    public void Configure(ShellBuilder builder, IConfiguration configuration) { }
}

public sealed class ToolingCodeDefaultTestDefaults : IEfToolingShellDefaults
{
    public void Configure(ShellBuilder builder, IConfiguration configuration) =>
        builder.WithFeature<RuntimeEntityFrameworkCoreFeature>()
            .WithConfiguration("Elsa:Persistence:DefaultResource", "primary");
}
