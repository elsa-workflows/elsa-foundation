using Acme.Widgets;
using Elsa.Cli.Worker;
using Elsa.Persistence.EntityFramework.Tooling;
using System.Text.Json;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// Binding the host's own tooling entry point, and the refusal for a host whose persistence build predates
/// it (FR-010).
/// </summary>
public sealed class ToolingEntryPointTests
{
    /// <summary>
    /// The decision is the entry point's presence, not a version string — a version cannot say whether a
    /// build carries a type — but the message still names the version the host pins and a version known to
    /// carry it, because those are the two numbers an operator needs to act.
    /// </summary>
    [Fact]
    public void A_persistence_build_with_no_entry_point_is_refused_naming_both_versions()
    {
        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.Resolve(typeof(object).Assembly, "4.0.0-preview.1", "4.0.0-preview.999"));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("host-tooling-entry-point-missing", refusal.Code);
        Assert.Contains("4.0.0-preview.1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("4.0.0-preview.999", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_pins_no_version_at_all_still_gets_a_message_it_can_act_on()
    {
        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.Resolve(typeof(object).Assembly, pinnedVersion: null, "4.0.0-preview.999"));

        Assert.Contains("version unknown", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosts_own_binding_table_decides_the_canonical_provider_and_its_engine_package()
    {
        var entryPoint = ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1");

        Assert.Equal("PostgreSql", entryPoint.CanonicalProvider("postgres"));
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", entryPoint.ProviderPackageId("PostgreSql"));
    }

    [Fact]
    public void An_unknown_provider_is_a_usage_refusal_rather_than_a_reflection_failure()
    {
        var entryPoint = ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1");

        var refusal = Assert.Throws<WorkerRefusal>(() => entryPoint.CanonicalProvider("Oracle"));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Equal("unknown-provider", refusal.Code);
    }

    /// <summary>
    /// The one constant the worker and the host's persistence assembly must spell identically while
    /// referencing nothing of each other (ADR 0076 D1). A drift here would silently read an empty selection
    /// out of a host that plainly made one, which is the failure that looks like agreement.
    /// </summary>
    [Fact]
    public void The_worker_and_the_persistence_assembly_name_the_same_capability_key()
    {
        Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalProviderBinding.CapabilitySelectionKey, HostCapabilitySelection.Key);
        Assert.Equal(EfProviderAgreement.CapabilityKey, HostCapabilitySelection.Key);
    }

    /// <summary>
    /// The version-skew probe (spec 172 FR-004). The tooling contract refuses an unmapped request property,
    /// so a build that predates the field must be found before the field is sent — and the refusal that
    /// follows names the key rather than reporting a malformed request.
    /// </summary>
    [Fact]
    public void A_persistence_build_carrying_the_capability_field_is_detected_and_one_without_it_refuses()
    {
        Assert.True(
            ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1").SupportsCapabilitySelection,
            "This build's own tooling contract carries the field.");

        var refusal = ToolingEntryPoint.CapabilitySelectionUnsupported(["PostgreSql"], "4.0.0-preview.1", "4.0.0-preview.999");

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("host-tooling-capability-unaware", refusal.Code);
        Assert.Contains(HostCapabilitySelection.Key, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'PostgreSql'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("4.0.0-preview.1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("4.0.0-preview.999", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_capability_requires_the_complete_exact_host_api()
    {
        var entryPoint = ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly,
            "4.0.0-preview.1", "4.0.0-preview.1");
        Assert.True(entryPoint.SupportsConfigurationContext);

        Assert.Null(ToolingEntryPoint.BindContextApi(typeof(LegacyHost), null, null));
        Assert.NotNull(ToolingEntryPoint.BindContextApi(typeof(CompleteContextHost),
            typeof(TestContext), typeof(CurrentContextProtocol)));

        foreach (var (host, context, protocol) in new (Type, Type?, Type?)[]
                 {
                     (typeof(PartialContextHost), typeof(TestContext), typeof(CurrentContextProtocol)),
                     (typeof(CompleteContextHost), typeof(TestContext), null),
                     (typeof(CompleteContextHost), typeof(TestContext), typeof(UnknownContextProtocol)),
                     (typeof(UnknownContextHost), typeof(UnknownVersionContext), typeof(CurrentContextProtocol))
                 })
        {
            var refusal = Assert.Throws<WorkerRefusal>(() =>
                ToolingEntryPoint.BindContextApi(host, context, protocol));
            Assert.Equal("context-capability-unavailable", refusal.Code);
            Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        }
    }

    [Fact]
    public async Task Reflected_context_invocation_returns_only_a_validated_typed_legacy_outcome()
    {
        var hostAssembly = typeof(EfToolingHost).Assembly;
        var entryPoint = ToolingEntryPoint.Resolve(hostAssembly, "4.0.0-preview.1", "4.0.0-preview.1");
        var descriptor = new
        {
            contextVersion = 1,
            source = "workbench-json-v1",
            hostDirectory = Path.GetDirectoryName(hostAssembly.Location),
            hostName = hostAssembly.GetName().Name,
            environment = "Production",
            shell = (string?)null,
            explicitSelection = false
        };
        var context = entryPoint.CreateConfigurationContext(descriptor, CancellationToken.None);
        try
        {
            var (exitCode, response, outcome) = await entryPoint.InspectConfigurationContextAsync(
                context, selection: null, CancellationToken.None);

            Assert.Equal(ToolExitCode.Success, exitCode);
            Assert.Equal("legacy-only", outcome);
            Assert.Equal("legacy-only", response.GetProperty("inspectContext").GetProperty("outcome").GetString());
            Assert.Equal("not-performed", response.GetProperty("configurationContext").GetProperty("targetVerification").GetString());
        }
        finally
        {
            ToolingEntryPoint.DisposeConfigurationContext(context);
        }
    }

    [Fact]
    public void Context_list_response_requires_a_closed_success_payload_and_redacted_context_facts()
    {
        const string valid = """
            {"version":2,"status":"ok","exitCode":0,"command":"list",
             "list":{"modules":[{"module":"Workflows.Runtime","assembly":"Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore",
               "context":"RuntimeDbContext","historyTable":"__EFMigrationsHistory_Runtime","dependsOn":[],"providers":["Sqlite"]}]},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production","shell":"default",
               "resource":"primary","resolution":"resource","targetVerification":"not-performed","runtimeParity":"unobserved",
               "participants":[],"unresolved":[]}}
            """;
        var accepted = JsonSerializer.Deserialize<HostContextInspectionResponse>(valid, WorkerContract.Json)!;
        Assert.Null(accepted.Validate("list", ToolExitCode.Success));

        foreach (var invalid in new[]
                 {
                     valid.Replace("\"list\":{", "\"inspectContext\":{},\"list\":{", StringComparison.Ordinal),
                     valid.Replace("\"targetVerification\":\"not-performed\"", "\"targetVerification\":\"matched\"", StringComparison.Ordinal),
                     valid.Replace("\"shell\":\"default\"", "\"shell\":null", StringComparison.Ordinal),
                     valid.Replace("\"providers\":[\"Sqlite\"]", "\"providers\":null", StringComparison.Ordinal)
                 })
        {
            var parsed = JsonSerializer.Deserialize<HostContextInspectionResponse>(invalid, WorkerContract.Json)!;
            Assert.Equal("context-capability-unavailable",
                Assert.Throws<WorkerRefusal>(() => parsed.Validate("list", ToolExitCode.Success)).Code);
        }
    }

    [Fact]
    public void Context_plan_response_requires_consistent_order_and_migration_counts()
    {
        const string valid = """
            {"version":2,"status":"ok","exitCode":0,"command":"plan",
             "plan":{"provider":"Sqlite","modules":[{"order":1,"module":"Workflows.Runtime",
               "assembly":"Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore","context":"RuntimeDbContext",
               "historyTable":"__EFMigrationsHistory_Runtime","from":"0","to":"Initial",
               "count":1,"ids":["Initial"],"dependsOn":[]}]},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production","shell":"default",
               "resource":"primary","resolution":"resource","targetVerification":"not-performed","runtimeParity":"unobserved",
               "participants":[],"unresolved":["expected-connection-unchecked"]}}
            """;
        var accepted = JsonSerializer.Deserialize<HostContextInspectionResponse>(valid, WorkerContract.Json)!;
        Assert.Null(accepted.Validate("plan", ToolExitCode.Success));

        foreach (var invalid in new[]
                 {
                     valid.Replace("\"order\":1", "\"order\":2", StringComparison.Ordinal),
                     valid.Replace("\"count\":1", "\"count\":2", StringComparison.Ordinal),
                     valid.Replace("\"targetVerification\":\"not-performed\"", "\"targetVerification\":\"matched\"", StringComparison.Ordinal)
                 })
        {
            var parsed = JsonSerializer.Deserialize<HostContextInspectionResponse>(invalid, WorkerContract.Json)!;
            Assert.Equal("context-capability-unavailable",
                Assert.Throws<WorkerRefusal>(() => parsed.Validate("plan", ToolExitCode.Success)).Code);
        }
    }

    [Fact]
    public void Live_context_response_requires_a_matched_target_and_one_typed_payload()
    {
        const string valid = """
            {"version":2,"status":"ok","exitCode":0,"command":"apply",
             "apply":{"provider":"Sqlite","modules":[{"order":1,"module":"Workflows.Runtime",
               "context":"RuntimeDbContext","historyTable":"__EFMigrationsHistory_Runtime","applied":[]}]},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production","shell":"default",
               "resource":"primary","resolution":"resource","targetVerification":"matched","runtimeParity":"unobserved",
               "participants":[],"unresolved":[]}}
            """;
        Assert.Null(JsonSerializer.Deserialize<HostContextInspectionResponse>(valid, WorkerContract.Json)!
            .Validate("apply", ToolExitCode.Success));

        foreach (var invalid in new[]
                 {
                     valid.Replace("\"targetVerification\":\"matched\"", "\"targetVerification\":\"not-performed\"", StringComparison.Ordinal),
                     valid.Replace("\"applied\":[]", "\"applied\":null", StringComparison.Ordinal),
                     valid.Replace("\"apply\":{", "\"validate\":{},\"apply\":{", StringComparison.Ordinal),
                     valid.Replace("\"order\":1", "\"order\":2", StringComparison.Ordinal)
                 })
        {
            var parsed = JsonSerializer.Deserialize<HostContextInspectionResponse>(invalid, WorkerContract.Json)!;
            Assert.Equal("context-capability-unavailable",
                Assert.Throws<WorkerRefusal>(() => parsed.Validate("apply", ToolExitCode.Success)).Code);
        }
    }

    [Fact]
    public void Script_context_response_requires_one_typed_payload_and_offline_target_evidence()
    {
        const string valid = """
            {"version":2,"status":"ok","exitCode":0,"command":"script",
             "script":{"manifest":"migration-plan.json","manifestSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
               "files":[{"order":1,"module":"Workflows.Runtime","file":"01-workflows-runtime.sql",
                 "sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production","shell":"default",
               "resource":"primary","resolution":"resource","targetVerification":"not-performed","runtimeParity":"unobserved",
               "participants":[],"unresolved":["expected-connection-unchecked"]}}
            """;
        Assert.Null(JsonSerializer.Deserialize<HostContextInspectionResponse>(valid, WorkerContract.Json)!
            .Validate("script", ToolExitCode.Success));

        foreach (var invalid in new[]
                 {
                     valid.Replace("\"targetVerification\":\"not-performed\"", "\"targetVerification\":\"matched\"", StringComparison.Ordinal),
                     valid.Replace("\"script\":{", "\"plan\":{},\"script\":{", StringComparison.Ordinal),
                     valid.Replace("\"order\":1", "\"order\":2", StringComparison.Ordinal),
                     valid.Replace("\"manifestSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"",
                         "\"manifestSha256\":\"invalid\"", StringComparison.Ordinal)
                 })
        {
            var parsed = JsonSerializer.Deserialize<HostContextInspectionResponse>(invalid, WorkerContract.Json)!;
            Assert.Equal("context-capability-unavailable",
                Assert.Throws<WorkerRefusal>(() => parsed.Validate("script", ToolExitCode.Success)).Code);
        }
    }

    [Fact]
    public void Reflected_factory_failure_is_redacted_without_a_context_fallback()
    {
        var hostAssembly = typeof(EfToolingHost).Assembly;
        var entryPoint = ToolingEntryPoint.Resolve(hostAssembly, "4.0.0-preview.1", "4.0.0-preview.1");
        var descriptor = new
        {
            contextVersion = 1,
            source = "secret-canary-invalid-source",
            hostDirectory = Path.GetDirectoryName(hostAssembly.Location),
            hostName = hostAssembly.GetName().Name,
            environment = "Production",
            shell = (string?)null,
            explicitSelection = false
        };

        var refusal = Assert.Throws<WorkerRefusal>(() =>
            entryPoint.CreateConfigurationContext(descriptor, CancellationToken.None));

        Assert.Equal("configuration-context-invalid", refusal.Code);
        Assert.DoesNotContain("secret-canary", refusal.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(descriptor.hostDirectory!, refusal.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture module is a third-party one, and the entry point it is discovered through is the host's
    /// own: nothing about this assembly is known to Elsa (spec 171 User Story 6).
    /// </summary>
    [Fact]
    public void The_third_party_fixture_declares_a_module_the_hosts_own_catalog_discovers()
    {
        var descriptor = Assert.Single(Elsa.Persistence.EntityFramework.EfModuleCatalog.Discover([typeof(WidgetsDbContext).Assembly]));

        Assert.Equal("Acme.Widgets", descriptor.Name);
        Assert.Equal("__EFMigrationsHistory_AcmeWidgets", descriptor.HistoryTableName);
        Assert.Null(descriptor.ProviderContext("MySql"));
    }

    private static class LegacyHost { }

    private sealed class TestContext : IDisposable
    {
        public const int Version = 1;
        public void Dispose() { }
    }

    private sealed class UnknownVersionContext : IDisposable
    {
        public const int Version = 99;
        public void Dispose() { }
    }

    private static class CurrentContextProtocol
    {
        public const int Version = 2;
    }

    private static class UnknownContextProtocol
    {
        public const int Version = 99;
    }

    private static class PartialContextHost
    {
        public static TestContext CreateConfigurationContext(Stream request, CancellationToken cancellationToken) => new();
    }

    private static class CompleteContextHost
    {
        public static TestContext CreateConfigurationContext(Stream request, CancellationToken cancellationToken) => new();
        public static Task<int> RunAsync(Stream request, Stream response, TestContext context,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private static class UnknownContextHost
    {
        public static UnknownVersionContext CreateConfigurationContext(Stream request, CancellationToken cancellationToken) => new();
        public static Task<int> RunAsync(Stream request, Stream response, UnknownVersionContext context,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
