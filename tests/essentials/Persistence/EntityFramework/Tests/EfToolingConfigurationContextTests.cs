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

    [Fact]
    public void Unselected_host_probe_evaluates_code_defaults_and_requires_an_explicit_context()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """
            { "Elsa": { "Persistence": {
              "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } }
            } }, "CShells": { "Shells": { "default": { "Name": "default" } } } }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);

        var refusal = Assert.Throws<EfToolingRefusal>(() => context.InspectUnselectedHost(
            new ToolingCodeDefaultTestDefaults(), RuntimeFeatureAssemblies, CancellationToken.None));

        Assert.Equal("configuration-context-required", refusal.Code);
        Assert.DoesNotContain("Shared", refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unselected_host_probe_checks_resource_intent_in_every_configured_shell()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """
            { "Elsa": { "Persistence": {
              "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
              "DefaultResource": "primary"
            } }, "CShells": { "Shells": {
              "first": { "Name": "first" },
              "second": { "Name": "second", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} } }
            } } }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);

        Assert.Equal("configuration-context-required", Assert.Throws<EfToolingRefusal>(() =>
            context.InspectUnselectedHost(new ToolingContextTestDefaults(),
                RuntimeFeatureAssemblies, CancellationToken.None)).Code);
    }

    [Fact]
    public void Unselected_host_probe_distinguishes_inert_definitions_from_composer_free_hints()
    {
        using var host = new HostFiles();
        host.WriteRaw("appsettings.json", """
            { "Elsa": { "Persistence": {
              "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } }
            } }, "CShells": { "Shells": { "default": { "Name": "default" } } } }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);

        var inspection = context.InspectUnselectedHost(new ToolingContextTestDefaults(),
            RuntimeFeatureAssemblies, CancellationToken.None);
        Assert.Equal("no-resource-applicable", inspection.Outcome);
        Assert.Equal("host-composition-unavailable", Assert.Throws<EfToolingRefusal>(() =>
            context.InspectUnselectedHost(null, RuntimeFeatureAssemblies, CancellationToken.None)).Code);
    }

    [Fact]
    public void Composer_free_probe_without_resource_hints_is_legacy_only()
    {
        using var host = new HostFiles();
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);

        var inspection = context.InspectUnselectedHost(null, RuntimeFeatureAssemblies, CancellationToken.None);

        Assert.Equal("legacy-only", inspection.Outcome);
        Assert.Contains("host-not-enrolled", inspection.UnresolvedCodes);
    }

    [Fact]
    public void Composer_free_probe_refuses_a_null_shell_resource_hint()
    {
        using var host = new HostFiles();
        host.WriteRaw("shells.json", """
            { "cshells": { "shells": { "default": { "configuration": {
              "elsa": { "persistence": { "defaultResource": null } }
            } } } } }
            """);
        using var context = host.Create(EfToolingConfigurationContext.WorkbenchJson,
            shell: null, explicitSelection: false);

        Assert.Equal("host-composition-unavailable", Assert.Throws<EfToolingRefusal>(() =>
            context.InspectUnselectedHost(null, RuntimeFeatureAssemblies, CancellationToken.None)).Code);
    }

    [Fact]
    public async Task Context_operation_returns_a_closed_typed_negative_inspection()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
              "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } }
            } }, "CShells": { "Shells": { "default": { "Name": "default" } } } }
            """);
        using var request = new MemoryStream("""{"version":2,"command":"inspect-context"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("no-resource-applicable", root.GetProperty("inspectContext").GetProperty("outcome").GetString());
        var facts = root.GetProperty("configurationContext");
        Assert.Equal("no-resource-applicable", facts.GetProperty("resolution").GetString());
        Assert.Equal("not-performed", facts.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", facts.GetProperty("runtimeParity").GetString());
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("shell").ValueKind);
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("resource").ValueKind);
        Assert.Equal(0, facts.GetProperty("participants").GetArrayLength());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Context_list_selects_the_named_resource_from_the_composed_shell_without_reading_its_connection()
    {
        using var context = ToolingContextForTestAssembly("""
            { "ConnectionStrings": { "Shared": "connection-value-canary" },
              "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"list","selection":{"kind":"from-host"},"resource":"primary"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        var json = Encoding.UTF8.GetString(response.ToArray());
        Assert.DoesNotContain("connection-value-canary", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Workflows.Runtime", Assert.Single(root.GetProperty("list").GetProperty("modules").EnumerateArray()).GetProperty("module").GetString());
        var facts = root.GetProperty("configurationContext");
        Assert.Equal("resource", facts.GetProperty("resolution").GetString());
        Assert.Equal("primary", facts.GetProperty("resource").GetString());
        Assert.Equal("not-performed", facts.GetProperty("targetVerification").GetString());
        Assert.Equal("Shared", Assert.Single(facts.GetProperty("participants").EnumerateArray()).GetProperty("connectionReference").GetString());
    }

    [Fact]
    public async Task Context_list_refuses_an_unrelated_resource_before_any_listing()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": {
                  "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" },
                  "unused": { "Provider": "Sqlite", "ConnectionName": "Other" }
                }, "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"list","resource":"unused"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("resource-target-scope", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(document.RootElement.TryGetProperty("list", out _));
    }

    [Fact]
    public async Task Context_plan_uses_the_selected_resource_without_an_expected_connection_value()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "MissingUntilDeployment" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"plan","selection":{"kind":"from-host"},"resource":"primary","provider":"Sqlite"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        var root = document.RootElement;
        var plan = root.GetProperty("plan");
        Assert.Equal("Sqlite", plan.GetProperty("provider").GetString());
        Assert.Equal("Workflows.Runtime", Assert.Single(plan.GetProperty("modules").EnumerateArray()).GetProperty("module").GetString());
        var facts = root.GetProperty("configurationContext");
        Assert.Equal("not-performed", facts.GetProperty("targetVerification").GetString());
        Assert.Contains(facts.GetProperty("unresolved").EnumerateArray(),
            entry => entry.GetString() == "expected-connection-unchecked");
    }

    [Fact]
    public async Task Context_plan_refuses_provider_disagreement_before_building_a_plan()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"plan","selection":{"kind":"from-host"},"resource":"primary","provider":"PostgreSql"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("provider-disagreement", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(document.RootElement.TryGetProperty("plan", out _));
    }

    [Fact]
    public async Task Context_plan_without_a_resource_preserves_legacy_from_host_selection()
    {
        using var context = ToolingContextForTestAssembly("""
            { "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"plan","selection":{"kind":"from-host"},"provider":"Sqlite"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("Workflows.Runtime", Assert.Single(document.RootElement.GetProperty("plan").GetProperty("modules").EnumerateArray())
            .GetProperty("module").GetString());
        Assert.Equal("legacy", document.RootElement.GetProperty("configurationContext").GetProperty("resolution").GetString());
    }

    [Fact]
    public async Task Context_plan_checks_the_composed_legacy_provider_for_every_selected_owner()
    {
        using var context = ToolingContextForTestAssembly("""
            { "CShells": { "Shells": { "default": {
                "Name": "default", "Features": {
                  "WorkflowsRuntimeEntityFrameworkCore": { "Provider": "PostgreSql" }
                }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"plan","selection":{"kind":"from-host"},"provider":"Sqlite"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("provider-disagreement", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Context_script_requires_explicit_resource_selection_before_writing_an_artifact()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        var output = Path.Combine(Path.GetTempPath(), $"elsa-context-script-{Guid.NewGuid():N}");
        using var request = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 2, command = "script", selection = new { kind = "from-host" },
            provider = "Sqlite", output,
            engine = new { package = "unused", version = "unused", source = "host-deps-file" },
            packages = Array.Empty<object>()
        }));
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        Assert.False(Directory.Exists(output));
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("resource-required", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    public async Task Live_context_refuses_a_mismatched_named_target_before_database_access(string command)
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-context-{Guid.NewGuid():N}.db");
        var actual = $"Data Source={path}";
        const string expected = "Data Source=expected-connection-value-canary.db";
        using var context = ToolingContextForTestAssembly($$"""
            { "ConnectionStrings": { "Shared": "{{expected}}" },
              "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 2, command, selection = new { kind = "from-host" }, resource = "primary",
            provider = "Sqlite", connection = actual
        }));
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        var json = Encoding.UTF8.GetString(response.ToArray());
        Assert.DoesNotContain(expected, json, StringComparison.Ordinal);
        Assert.DoesNotContain(actual, json, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("connection-target-mismatch", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Live_context_requires_an_explicit_resource_when_a_selected_owner_uses_one()
    {
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream("""{"version":2,"command":"apply","selection":{"kind":"from-host"},"provider":"Sqlite","connection":"Data Source=unused.db"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("resource-required", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Live_context_refuses_an_unresolved_expected_connection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-context-{Guid.NewGuid():N}.db");
        using var context = ToolingContextForTestAssembly("""
            { "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "MissingUntilDeployment" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """, shell: "default", explicitSelection: true);
        using var request = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 2, command = "apply", selection = new { kind = "from-host" }, resource = "primary",
            provider = "Sqlite", connection = $"Data Source={path}"
        }));
        using var response = new MemoryStream();

        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        Assert.False(File.Exists(path));
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("expected-connection-unresolved", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Live_context_applies_and_validates_a_matched_disposable_sqlite_target()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-context-{Guid.NewGuid():N}.db");
        var connection = $"Data Source={path}";
        try
        {
            using var context = ToolingContextForTestAssembly($$"""
                { "ConnectionStrings": { "Shared": "{{connection}}" },
                  "Elsa": { "Persistence": {
                    "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                    "DefaultResource": "primary"
                  } }, "CShells": { "Shells": { "default": {
                    "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
                  } } } }
                """, shell: "default", explicitSelection: true);

            foreach (var command in new[] { "apply", "validate" })
            {
                using var request = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    version = 2, command, selection = new { kind = "from-host" }, resource = "primary",
                    provider = "Sqlite", connection
                }));
                using var response = new MemoryStream();
                var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
                    [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

                Assert.Equal(EfToolingExitCode.Success, exitCode);
                var json = Encoding.UTF8.GetString(response.ToArray());
                Assert.DoesNotContain(connection, json, StringComparison.Ordinal);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Assert.Equal("matched", root.GetProperty("configurationContext").GetProperty("targetVerification").GetString());
                Assert.Equal("unobserved", root.GetProperty("configurationContext").GetProperty("runtimeParity").GetString());
                Assert.DoesNotContain(root.GetProperty("configurationContext").GetProperty("unresolved").EnumerateArray(),
                    value => value.GetString() == "expected-connection-unchecked");
                Assert.Equal("Workflows.Runtime", Assert.Single(root.GetProperty(command).GetProperty("modules").EnumerateArray())
                    .GetProperty("module").GetString());
            }
            Assert.True(File.Exists(path));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Context_operation_refuses_positive_intent_and_closed_shape_without_secret_echo()
    {
        using var context = ToolingContextForTestAssembly("""
            { "ConnectionStrings": { "Shared": "connection-value-canary" },
              "Elsa": { "Persistence": {
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" } },
                "DefaultResource": "primary"
              } }, "CShells": { "Shells": { "default": {
                "Name": "default", "Features": { "WorkflowsRuntimeEntityFrameworkCore": {} }
              } } } }
            """);
        var cases = new (string Payload, string Code)[]
                 {
                     ("""{"version":2,"command":"inspect-context"}""", "configuration-context-required"),
                     ("""{"version":2,"command":"inspect-context","host":{}}""", "invalid-request"),
                     ("""{"version":2,"command":"inspect-context","shells":[]}""", "invalid-request"),
                     ("""{"version":2,"command":"inspect-context","capabilitySelection":[]}""", "invalid-request"),
                     ("""{"version":2,"version":2,"command":"inspect-context"}""", "invalid-request"),
                     ("""{"version":1,"command":"inspect-context"}""", "unsupported-request-version")
                 };
        foreach (var (payload, code) in cases)
        {
            using var request = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            using var response = new MemoryStream();
            var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
                [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);

            Assert.NotEqual(EfToolingExitCode.Success, exitCode);
            var json = Encoding.UTF8.GetString(response.ToArray());
            Assert.DoesNotContain("connection-value-canary", json, StringComparison.Ordinal);
            Assert.DoesNotContain(context.HostDirectory, json, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json);
            var error = document.RootElement.GetProperty("error");
            Assert.Equal(code, error.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Context_operation_reports_composer_free_legacy_only_without_claiming_a_graph_check()
    {
        var hostAssembly = typeof(EfToolingHost).Assembly;
        using var context = new EfToolingConfigurationContext(
            EfToolingConfigurationContext.WorkbenchJson,
            Path.GetDirectoryName(hostAssembly.Location)!, hostAssembly.GetName().Name!,
            "Production", null, false, new ConfigurationBuilder().Build());
        using var request = new MemoryStream("""{"version":2,"command":"inspect-context"}"""u8.ToArray());
        using var response = new MemoryStream();

        var exitCode = await EfToolingHost.RunAsync(request, response, context, CancellationToken.None);

        Assert.Equal(EfToolingExitCode.Success, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        var root = document.RootElement;
        Assert.Equal("legacy-only", root.GetProperty("inspectContext").GetProperty("outcome").GetString());
        Assert.Equal("legacy-only", root.GetProperty("configurationContext").GetProperty("resolution").GetString());
        Assert.Contains(root.GetProperty("configurationContext").GetProperty("unresolved").EnumerateArray(),
            item => item.GetString() == "host-not-enrolled");
    }

    [Fact]
    public void Context_operation_has_the_exact_reflected_public_signature()
    {
        var method = typeof(EfToolingHost).GetMethod("RunAsync",
            [typeof(Stream), typeof(Stream), typeof(EfToolingConfigurationContext), typeof(CancellationToken)]);

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<int>), method.ReturnType);
    }

    [Fact]
    public async Task Context_operation_preserves_cancellation_and_refuses_a_disposed_snapshot()
    {
        using var context = ToolingContextForTestAssembly("""
            { "CShells": { "Shells": { "default": { "Name": "default" } } } }
            """);
        using var cancelledRequest = new MemoryStream("""{"version":2,"command":"inspect-context"}"""u8.ToArray());
        using var cancelledResponse = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EfToolingContextOperation.RunAsync(
            cancelledRequest, cancelledResponse, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], cancellation.Token));
        Assert.Equal(0, cancelledResponse.Length);

        context.Dispose();
        using var request = new MemoryStream("""{"version":2,"command":"inspect-context"}"""u8.ToArray());
        using var response = new MemoryStream();
        var exitCode = await EfToolingContextOperation.RunAsync(request, response, context,
            [typeof(EfToolingConfigurationContextTests).Assembly, .. RuntimeFeatureAssemblies], CancellationToken.None);
        Assert.Equal(EfToolingExitCode.ResolutionFailure, exitCode);
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal("configuration-context-disposed", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static EfToolingConfigurationContext ToolingContextForTestAssembly(
        string json, string? shell = null, bool explicitSelection = false)
    {
        var hostAssembly = typeof(EfToolingConfigurationContextTests).Assembly;
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(source).Build();
        return new EfToolingConfigurationContext(
            EfToolingConfigurationContext.WorkbenchJson,
            Path.GetDirectoryName(hostAssembly.Location)!, hostAssembly.GetName().Name!,
            "Production", shell, explicitSelection, configuration);
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
