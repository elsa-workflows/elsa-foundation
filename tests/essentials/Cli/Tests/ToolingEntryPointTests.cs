using Acme.Widgets;
using Elsa.Cli.Worker;
using Elsa.Persistence.EntityFramework.Tooling;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// Binding the host's own tooling entry point, and the refusal for a host whose persistence build predates
/// it (FR-010).
/// </summary>
public sealed class ToolingEntryPointTests : IDisposable
{
    private readonly List<string> candidateInvocationIds = [];

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
    /// The same probe for the skew allowance <c>status</c> judges members with: this build's contract carries it, so the
    /// worker may send it, and a build that predates it is never sent a field its closed contract would refuse.
    /// </summary>
    [Fact]
    public void A_persistence_build_carrying_the_skew_allowance_field_is_detected()
    {
        Assert.True(ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1").SupportsSkewAllowance);
    }

    /// <summary>The same probe for the lock bound <c>apply</c> waits for a SQLite migration lock with (#2196).</summary>
    [Fact]
    public void A_persistence_build_carrying_the_sqlite_migration_lock_bound_field_is_detected()
    {
        Assert.True(ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1").SupportsSqliteMigrationLockStaleAfter);
    }

    /// <summary>
    /// A persistence build that predates the lock bound is found not to declare it, and the worker then leaves the field out of
    /// the request, with a warning, rather than send what that build's closed contract would refuse; a configured bound it can send is
    /// sent in the invariant <c>c</c> format, and none configured sends nothing and says nothing.
    /// </summary>
    [Fact]
    public void A_persistence_build_without_the_sqlite_migration_lock_bound_field_is_detected_and_the_worker_omits_it()
    {
        Assert.False(ToolingEntryPoint.Declares(typeof(OlderRequest), "SqliteMigrationLockStaleAfter"));
        Assert.False(ToolingEntryPoint.Declares(null, "SqliteMigrationLockStaleAfter"));
        Assert.True(ToolingEntryPoint.Declares(typeof(EfToolingRequest), "SqliteMigrationLockStaleAfter"));

        var method = typeof(object).GetMethod(nameof(ToString))!;
        var older = new ToolingEntryPoint(method, method, method, method, supportsCapabilitySelection: true, contextApi: null, supportsSkewAllowance: true, supportsSqliteMigrationLockStaleAfter: false);
        var current = new ToolingEntryPoint(method, method, method, method, supportsCapabilitySelection: true, contextApi: null, supportsSkewAllowance: true, supportsSqliteMigrationLockStaleAfter: true);
        var configured = new WorkerRequest { SqliteMigrationLockStaleAfter = TimeSpan.FromMinutes(30) };

        var warnings = new StringWriter();
        Assert.Null(WorkerRunner.SqliteMigrationLockStaleAfter(older, configured, warnings));
        var warning = warnings.ToString().TrimEnd();
        Assert.DoesNotContain('\n', warning);
        Assert.StartsWith("warning:", warning, StringComparison.Ordinal);
        Assert.Contains("00:30:00", warning, StringComparison.Ordinal);

        var quiet = new StringWriter();
        Assert.Equal("00:30:00", WorkerRunner.SqliteMigrationLockStaleAfter(current, configured, quiet));
        Assert.Null(WorkerRunner.SqliteMigrationLockStaleAfter(older, new WorkerRequest(), quiet));
        Assert.Null(WorkerRunner.SqliteMigrationLockStaleAfter(current, new WorkerRequest(), quiet));
        Assert.Empty(quiet.ToString());
    }

    /// <summary>A request type of a persistence build that predates the lock bound.</summary>
    private sealed class OlderRequest
    {
        public string? CapabilitySelection { get; init; }
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
    public void Context_response_json_refuses_unmapped_fields()
    {
        const string response = """
            {"version":2,"status":"ok","exitCode":0,"command":"inspect-context",
             "inspectContext":{"outcome":"legacy-only"},
             "configurationContext":{"source":"workbench-json-v1","environment":"Production",
               "shell":null,"resource":null,"resolution":"legacy-only","targetVerification":"not-performed",
               "runtimeParity":"unobserved","participants":[],"unresolved":[]},
             "futureInstruction":"must not be silently ignored"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<HostContextInspectionResponse>(response, WorkerContract.Json));
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

    [Fact]
    public void Context_disposal_failure_is_redacted_and_names_the_context_boundary()
    {
        const string sentinel = "context-disposal-secret-canary";
        var context = new ThrowingDisposeContext(sentinel);

        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.DisposeConfigurationContext(context));

        Assert.Equal("configuration-context-invalid", refusal.Code);
        Assert.Equal(1, context.DisposeCount);
        Assert.DoesNotContain(sentinel, refusal.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public void Candidate_capability_requires_the_exact_independently_versioned_streamed_api()
    {
        var operation = ToolingEntryPoint.BindCandidateInspection(typeof(CompleteCandidateHost), typeof(CurrentCandidateProtocol));

        Assert.Equal("RunCandidateInspectionAsync", operation.Name);
        Assert.Equal(typeof(Task<int>), operation.ReturnType);
        Assert.Equal(typeof(CompleteCandidateHost), operation.DeclaringType);
        Assert.Equal(new[] { typeof(Stream), typeof(Stream), typeof(CancellationToken) },
            operation.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData("missing-host")]
    [InlineData("legacy-host")]
    [InlineData("missing-contract")]
    [InlineData("wrong-version")]
    [InlineData("mutable-version")]
    [InlineData("wrong-return")]
    [InlineData("wrong-signature")]
    [InlineData("instance-method")]
    [InlineData("generic-method")]
    [InlineData("wrong-version-type")]
    public void Candidate_capability_refuses_old_partial_or_skewed_hosts_without_legacy_fallback(string scenario)
    {
        var host = scenario switch
        {
            "missing-host" => null,
            "legacy-host" => typeof(LegacyHost),
            "wrong-return" => typeof(WrongReturnCandidateHost),
            "wrong-signature" => typeof(WrongSignatureCandidateHost),
            "instance-method" => typeof(InstanceCandidateHost),
            "generic-method" => typeof(GenericCandidateHost),
            _ => typeof(CompleteCandidateHost)
        };
        var protocol = scenario switch
        {
            "missing-contract" => null,
            "wrong-version" => typeof(UnknownCandidateProtocol),
            "mutable-version" => typeof(MutableCandidateProtocol),
            "wrong-version-type" => typeof(WrongTypeCandidateProtocol),
            _ => typeof(CurrentCandidateProtocol)
        };

        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.BindCandidateInspection(host, protocol));

        Assert.Equal("candidate-capability-unavailable", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("The selected host has no complete candidate inspection capability.", refusal.Message);
    }

    [Fact]
    public void Candidate_environment_capability_requires_the_exact_independently_versioned_streamed_api()
    {
        var operation = ToolingEntryPoint.BindCandidateEnvironmentInspection(
            typeof(CompleteCandidateEnvironmentHost), typeof(CurrentCandidateEnvironmentProtocol));

        Assert.Equal("RunCandidateEnvironmentInspectionAsync", operation.Name);
        Assert.Equal(typeof(Task<int>), operation.ReturnType);
        Assert.Equal(typeof(CompleteCandidateEnvironmentHost), operation.DeclaringType);
        Assert.Equal(new[] { typeof(Stream), typeof(Stream), typeof(CancellationToken) },
            operation.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Candidate_environment_resolver_selects_the_additive_contract_from_the_selected_persistence_assembly()
    {
        var persistence = CandidateEnvironmentCapabilityAssembly();

        var operation = ToolingEntryPoint.ResolveCandidateEnvironmentInspection(persistence);

        Assert.Equal("RunCandidateEnvironmentInspectionAsync", operation.Name);
        Assert.Equal(typeof(Task<int>), operation.ReturnType);
        Assert.Equal("Elsa.Persistence.EntityFramework.Tooling.EfToolingHost", operation.DeclaringType!.FullName);
    }

    [Theory]
    [InlineData("missing-host")]
    [InlineData("old-candidate-only")]
    [InlineData("wrong-version-type")]
    [InlineData("missing-contract")]
    [InlineData("wrong-version")]
    [InlineData("mutable-version")]
    [InlineData("wrong-return")]
    [InlineData("wrong-signature")]
    [InlineData("instance-method")]
    [InlineData("generic-method")]
    public void Candidate_environment_capability_refuses_partial_or_skewed_hosts_without_legacy_fallback(string scenario)
    {
        var host = scenario switch
        {
            "missing-host" => null,
            "old-candidate-only" => typeof(CompleteCandidateHost),
            "wrong-return" => typeof(WrongReturnCandidateEnvironmentHost),
            "wrong-signature" => typeof(WrongSignatureCandidateEnvironmentHost),
            "instance-method" => typeof(InstanceCandidateEnvironmentHost),
            "generic-method" => typeof(GenericCandidateEnvironmentHost),
            _ => typeof(CompleteCandidateEnvironmentHost)
        };
        var protocol = scenario switch
        {
            "missing-contract" => null,
            "wrong-version" => typeof(UnknownCandidateEnvironmentProtocol),
            "mutable-version" => typeof(MutableCandidateEnvironmentProtocol),
            "wrong-version-type" => typeof(WrongTypeCandidateProtocol),
            _ => typeof(CurrentCandidateEnvironmentProtocol)
        };

        var refusal = Assert.Throws<WorkerRefusal>(() =>
            ToolingEntryPoint.BindCandidateEnvironmentInspection(host, protocol));

        Assert.Equal("candidate-capability-unavailable", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("The selected host has no complete candidate inspection capability.", refusal.Message);
    }

    [Fact]
    public void Candidate_environment_enrollment_reads_exact_metadata_without_running_the_attribute_constructor()
    {
        var persistence = EnrollmentAttributeAssembly();
        var host = HostAssemblyWithEnrollment(persistence);

        ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(host, persistence);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-version")]
    [InlineData("wrong-policy")]
    [InlineData("duplicate")]
    [InlineData("named-argument")]
    [InlineData("wrong-constructor")]
    [InlineData("wrong-assembly")]
    public void Candidate_environment_enrollment_refuses_missing_or_malformed_metadata(string scenario)
    {
        var selectedPersistence = EnrollmentAttributeAssembly(
            exactConstructor: scenario != "wrong-constructor", namedProperty: scenario == "named-argument");
        var host = scenario switch
        {
            "missing" => EmptyMetadataAssembly(),
            "wrong-version" => HostAssemblyWithEnrollment(selectedPersistence, version: 2),
            "wrong-policy" => HostAssemblyWithEnrollment(selectedPersistence, policy: "other-policy"),
            "duplicate" => HostAssemblyWithEnrollment(selectedPersistence, declarations: 2),
            "named-argument" => HostAssemblyWithEnrollment(selectedPersistence, namedArgument: true),
            "wrong-constructor" => HostAssemblyWithEnrollment(selectedPersistence),
            "wrong-assembly" => HostAssemblyWithEnrollment(EnrollmentAttributeAssembly()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var refusal = Assert.Throws<WorkerRefusal>(() =>
            ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(host, selectedPersistence));

        Assert.Equal("candidate-environment-host-unenrolled", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("The selected host is not enrolled for explicit environment inspection.", refusal.Message);
    }

    [Fact]
    public void Inspection_host_loader_returns_the_selected_assembly_and_preserves_the_legacy_void_signature()
    {
        var layout = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        var assembly = HostClosure.LoadHostAssemblyForInspection(layout.Directory, layout.Name);

        Assert.Equal(layout.Name, assembly.GetName().Name);
        Assert.Equal(Path.GetFullPath(Path.Join(layout.Directory, $"{layout.Name}.dll")),
            Path.GetFullPath(assembly.Location));
        Assert.Equal(typeof(void), typeof(HostClosure).GetMethod(nameof(HostClosure.LoadHostAssembly),
            [typeof(string), typeof(string)])!.ReturnType);
    }

    [Fact]
    public void Inspection_host_loader_accepts_a_parent_directory_alias_for_the_selected_host()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Use a host assembly not shared by the other loader controls so its default-context identity does
        // not make the location assertions order-dependent when the test class is run in parallel.
        var source = HostLayout.Resolve(DotnetElsa.Host("MinimalHost"));
        using var directory = new TempDirectory("elsa-cli-inspection-alias-");
        var alias = Path.Join(directory.Path, "host-parent");
        Directory.CreateSymbolicLink(alias, Path.GetDirectoryName(source.Directory)!);
        var selectedDirectory = Path.Join(alias, Path.GetFileName(source.Directory));
        try
        {
            var assembly = HostClosure.LoadHostAssemblyForInspection(selectedDirectory, source.Name);

            Assert.Equal(source.Name, assembly.GetName().Name);
            Assert.Equal($"{source.Name}.dll", Path.GetFileName(assembly.Location));
        }
        finally
        {
            // Do not let the disposable test directory traverse an alias into the compiled host output.
            Directory.Delete(alias);
        }
    }

    [Fact]
    public void Inspection_host_loader_refuses_a_dll_whose_actual_name_differs_from_the_selected_name()
    {
        using var directory = new TempDirectory("elsa-cli-inspection-host-");
        var source = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        var selectedName = "Renamed.ResourceAwareLiveHost";
        File.Copy(Path.Join(source.Directory, $"{source.Name}.dll"),
            Path.Join(directory.Path, $"{selectedName}.dll"));

        var refusal = Assert.Throws<WorkerRefusal>(() =>
            HostClosure.LoadHostAssemblyForInspection(directory.Path, selectedName));

        Assert.Equal("candidate-host-unavailable", refusal.Code);
    }

    [Fact]
    public void Inspection_host_loader_refuses_a_same_named_assembly_already_loaded_from_another_location()
    {
        var source = HostLayout.Resolve(DotnetElsa.Host("ResourceAwareLiveHost"));
        var loaded = HostClosure.LoadHostAssemblyForInspection(source.Directory, source.Name);
        using var directory = new TempDirectory("elsa-cli-inspection-location-");
        File.Copy(loaded.Location, Path.Join(directory.Path, $"{source.Name}.dll"));

        // Legacy loading still admits the matching identity. The additive lane also binds its actual location.
        HostClosure.LoadHostAssembly(directory.Path, source.Name);
        var refusal = Assert.Throws<WorkerRefusal>(() =>
            HostClosure.LoadHostAssemblyForInspection(directory.Path, source.Name));
        Assert.Equal("candidate-host-unavailable", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
    }

    [Fact]
    public async Task Candidate_invocation_sends_a_closed_host_envelope_and_preserves_the_validated_response()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);
        var expectedResponse = CandidateHostState.SuccessResponse(payload.InvocationId!, payload.CaptureId!);

        var workerResponse = await InvokeCandidateApiAsync(HostMethod(nameof(CandidateHost.Success)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None);

        Assert.Equal(0, workerResponse.ExitCode);
        Assert.Null(workerResponse.Error);
        Assert.Equal(expectedResponse, workerResponse.Tooling!.Value.GetRawText());
        using var hostRequest = JsonDocument.Parse(state.LastRequest!);
        var root = hostRequest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("Example.Host", root.GetProperty("host").GetProperty("name").GetString());
        Assert.Equal("/compiled/host", root.GetProperty("host").GetProperty("directory").GetString());
        Assert.Equal(payload.InvocationId, root.GetProperty("candidate").GetProperty("invocationId").GetString());
        Assert.Equal(payload.CaptureId, root.GetProperty("candidate").GetProperty("captureId").GetString());
        Assert.Equal(3, root.GetProperty("candidate").GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task Candidate_environment_invocation_sends_the_original_document_and_uses_the_new_response_lane()
    {
        var payload = Candidate();
        var environmentInput = new WorkerEnvironmentInput
        {
            Version = 1,
            CaptureId = payload.CaptureId,
            Content = Convert.ToBase64String(CandidateInspectionFixture.EnvironmentDocument())
        };
        var state = CandidateHostState.For(payload);

        var workerResponse = await InvokeCandidateEnvironmentApiAsync(
            HostMethod(nameof(CandidateHost.EnvironmentSuccess)), "Example.Host", "/compiled/host",
            payload, environmentInput, CancellationToken.None);

        Assert.Equal(0, workerResponse.ExitCode);
        using var hostRequest = JsonDocument.Parse(state.LastRequest!);
        var root = hostRequest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(payload.CaptureId, root.GetProperty("environmentInput").GetProperty("captureId").GetString());
        var content = root.GetProperty("environmentInput").GetProperty("content").GetString();
        Assert.Equal(environmentInput.Content, content);
        Assert.False(root.GetProperty("environmentInput").TryGetProperty("sourcePath", out _));
        Assert.Equal("captured-workbench-json-explicit-environment-v1",
            workerResponse.Tooling!.Value.GetProperty("configurationResolution").GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("environment")]
    [InlineData("selection")]
    [InlineData("removal")]
    public async Task Candidate_invocation_refuses_valid_projection_for_different_candidate_contents(string mutation)
    {
        var payload = Candidate() with { RemovedFeatureIds = ["Removed"] };
        var state = CandidateHostState.For(payload);
        state.ExpectedCandidate = payload;
        Assert.Equal(0, (await InvokeCandidateApiAsync(HostMethod(nameof(CandidateHost.Success)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None)).ExitCode);
        state.TransformSuccess = response =>
        {
            var body = response["configurationResolution"]!.AsObject();
            var selection = body["selection"]!.AsObject();
            switch (mutation)
            {
                case "shell": body["shell"] = "OtherShell"; break;
                case "environment": body["environment"] = "OtherEnvironment"; break;
                case "selection":
                    foreach (var field in new[] { "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds" })
                        selection[field] = new JsonArray("OtherFeature");
                    body["participants"]![0]!["feature"] = "OtherFeature";
                    break;
                case "removal": selection["disabledFeatureIds"] = new JsonArray(); break;
            }
        };

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.Success)), "Example.Host", "/compiled/host", payload, CancellationToken.None));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("Other", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_invocation_accepts_additional_observed_disabled_features()
    {
        var payload = Candidate();
        CandidateHostState.For(payload).TransformSuccess = response =>
            response["configurationResolution"]!["selection"]!["disabledFeatureIds"]!.AsArray().Add("OtherDisabled");
        var result = await InvokeCandidateApiAsync(HostMethod(nameof(CandidateHost.Success)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("file-capture")]
    public async Task Candidate_invocation_refuses_an_invalid_expected_capture(string mutation)
    {
        var payload = Candidate();
        payload = mutation switch
        {
            "version" => payload with { Version = 99 },
            "source" => payload with { Source = "OtherSource" },
            _ => payload with { Files = payload.Files!.Select((file, index) => index == 0
                ? file with { CaptureId = "cccccccccccccccccccccccccccccccc" } : file).ToArray() }
        };
        CandidateHostState.For(payload).ExpectedCandidate = payload;
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.Success)), "Example.Host", "/compiled/host", payload, CancellationToken.None));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
    }

    [Fact]
    public void Candidate_response_validation_requires_an_expected_candidate() =>
        Assert.Throws<ArgumentNullException>(() => WorkerContract.ValidateCandidateHostResponse(default, null!, 0));

    [Fact]
    public async Task Candidate_invocation_preserves_a_valid_redacted_host_refusal()
    {
        var payload = Candidate();

        var workerResponse = await InvokeCandidateApiAsync(HostMethod(nameof(CandidateHost.Refused)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None);

        Assert.Equal(ToolExitCode.Refusal, workerResponse.ExitCode);
        Assert.Null(workerResponse.Error);
        Assert.Equal("candidate-capture-invalid", workerResponse.Tooling!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(nameof(CandidateHost.WrongCorrelation))]
    [InlineData(nameof(CandidateHost.MalformedResponse))]
    [InlineData(nameof(CandidateHost.ExitCodeMismatch))]
    public async Task Candidate_invocation_rejects_wrong_or_malformed_host_responses_without_echoing_input(string hostMethod)
    {
        var payload = Candidate();

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(HostMethod(hostMethod),
            "Example.Host", "/compiled/host", payload, CancellationToken.None));

        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.DoesNotContain("private-response-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_invocation_converts_reflection_failures_to_a_fixed_value_free_refusal()
    {
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.ThrowCanary)), "Example.Host", "/compiled/host", Candidate(), CancellationToken.None));

        Assert.Equal("candidate-host-unavailable", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.DoesNotContain("private-reflection-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(CandidateHost.ThrowOutOfMemory))]
    [InlineData(nameof(CandidateHost.FaultOutOfMemory))]
    public async Task Candidate_invocation_preserves_fatal_memory_failures(string hostMethod)
    {
        await Assert.ThrowsAsync<OutOfMemoryException>(() => InvokeCandidateApiAsync(
            HostMethod(hostMethod), "Example.Host", "/compiled/host", Candidate(), CancellationToken.None));
    }

    [Theory]
    [InlineData(nameof(CandidateHost.ThrowAccessViolation))]
    [InlineData(nameof(CandidateHost.FaultAccessViolation))]
    public async Task Candidate_invocation_preserves_fatal_access_failures(string hostMethod)
    {
        await Assert.ThrowsAsync<AccessViolationException>(() => InvokeCandidateApiAsync(
            HostMethod(hostMethod), "Example.Host", "/compiled/host", Candidate(), CancellationToken.None));
    }

    [Theory]
    [InlineData(nameof(CandidateHost.ThrowBadImage))]
    [InlineData(nameof(CandidateHost.FaultBadImage))]
    public async Task Candidate_invocation_redacts_malformed_host_images(string hostMethod)
    {
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(hostMethod), "Example.Host", "/compiled/host", Candidate(), CancellationToken.None));

        Assert.Equal("candidate-host-unavailable", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.DoesNotContain("private-image-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_invocation_honors_cancellation_before_and_during_host_execution()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);
        using var before = new CancellationTokenSource();
        before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.Success)), "Example.Host", "/compiled/host", payload, before.Token));
        Assert.Equal(0, state.InvocationCount);

        using var during = new CancellationTokenSource();
        state.CancelCurrent = during.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.CancelDuringCall)), "Example.Host", "/compiled/host", payload, during.Token));
        Assert.Equal(1, state.InvocationCount);
    }

    [Fact]
    public async Task Candidate_invocation_refuses_oversized_host_requests_before_calling_the_host()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.Success)), "Example.Host", new string('x', 8 * 1024 * 1024), payload, CancellationToken.None));

        Assert.Equal("candidate-request-too-large", refusal.Code);
        Assert.Equal(0, state.InvocationCount);
        Assert.DoesNotContain("AAAA", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_invocation_bounds_host_output_during_writes()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.WriteOversizedResponse)), "Example.Host", "/compiled/host", payload, CancellationToken.None));

        Assert.Equal("candidate-response-too-large", refusal.Code);
        Assert.True(state.OutputLimitBlocked);
    }

    [Fact]
    public async Task Candidate_invocation_refuses_a_method_with_the_wrong_shape_before_calling_it()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);
        var malformedMethod = typeof(WrongSignatureCandidateHost).GetMethod("RunCandidateInspectionAsync",
            BindingFlags.Public | BindingFlags.Static)!;

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            malformedMethod, "Example.Host", "/compiled/host", payload, CancellationToken.None));

        Assert.Equal("candidate-capability-unavailable", refusal.Code);
        Assert.Equal(0, state.InvocationCount);
    }

    [Fact]
    public async Task Candidate_invocation_keeps_an_overflow_refusal_when_the_host_catches_the_write_failure()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.WriteOversizedResponseAndSwallow)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None));

        Assert.Equal("candidate-response-too-large", refusal.Code);
        Assert.True(state.OutputLimitBlocked);
    }

    [Theory]
    [InlineData(nameof(CandidateHost.SeekPastResponseLimit))]
    [InlineData(nameof(CandidateHost.SetLengthPastResponseLimit))]
    public async Task Candidate_invocation_refuses_response_stream_growth_through_seek_or_length(string hostMethod)
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);

        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(hostMethod), "Example.Host", "/compiled/host", payload, CancellationToken.None));

        Assert.Equal("candidate-response-too-large", refusal.Code);
        Assert.True(state.OutputLimitBlocked);
    }

    [Fact]
    public async Task Candidate_invocation_keeps_the_request_read_only_after_serialization()
    {
        var payload = Candidate();
        var state = CandidateHostState.For(payload);

        var workerResponse = await InvokeCandidateApiAsync(HostMethod(nameof(CandidateHost.AttemptRequestWrite)),
            "Example.Host", "/compiled/host", payload, CancellationToken.None);

        Assert.Equal(ToolExitCode.Success, workerResponse.ExitCode);
        Assert.True(state.RequestWriteDenied);
    }

    [Fact]
    public async Task Candidate_invocation_redacts_a_host_that_disposes_its_response_stream()
    {
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() => InvokeCandidateApiAsync(
            HostMethod(nameof(CandidateHost.DisposeResponse)),
            "Example.Host", "/compiled/host", Candidate(), CancellationToken.None));

        Assert.Equal("candidate-host-unavailable", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
    }

    [Fact]
    public async Task Candidate_invocation_guards_all_required_arguments()
    {
        var hostMethod = HostMethod(nameof(CandidateHost.Success));
        var candidate = Candidate();

        await Assert.ThrowsAsync<ArgumentNullException>(() => InvokeCandidateApiAsync(null!, "Example.Host", "/compiled/host", candidate, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => InvokeCandidateApiAsync(hostMethod, null!, "/compiled/host", candidate, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => InvokeCandidateApiAsync(hostMethod, "Example.Host", null!, candidate, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => InvokeCandidateApiAsync(hostMethod, "Example.Host", "/compiled/host", null!, CancellationToken.None));
    }

    public void Dispose()
    {
        foreach (var invocationId in candidateInvocationIds)
            CandidateHostState.Remove(invocationId);
    }

    private WorkerCandidatePayload Candidate()
    {
        var invocation = Guid.NewGuid().ToString("N");
        var capture = Guid.NewGuid().ToString("N");
        CandidateHostState.Register(invocation);
        candidateInvocationIds.Add(invocation);
        var payload = new WorkerCandidatePayload
        {
            Version = 1,
            Source = "captured-workbench-json-v1",
            InvocationId = invocation,
            CaptureId = capture,
            Shell = "candidate-test",
            Environment = "Production",
            AcceptedFeatureIds = ["WorkflowsRuntimeEntityFrameworkCore"],
            RemovedFeatureIds = [],
            Files =
            [
                new WorkerCandidateFile { Name = "appsettings.json", CaptureId = capture, Content = Convert.ToBase64String("{}"u8.ToArray()) },
                new WorkerCandidateFile { Name = "shells.json", CaptureId = capture, Content = Convert.ToBase64String("{}"u8.ToArray()) },
                new WorkerCandidateFile { Name = "shells.Production.json", CaptureId = capture, Content = Convert.ToBase64String("{}"u8.ToArray()) }
            ]
        };
        CandidateHostState.For(invocation).ExpectedCandidate = payload;
        return payload;
    }

    private static Task<WorkerResponse> InvokeCandidateApiAsync(
        MethodInfo hostMethod,
        string hostName,
        string hostDirectory,
        WorkerCandidatePayload candidate,
        CancellationToken cancellationToken)
        => ToolingEntryPoint.InvokeCandidateInspectionAsync(hostMethod, hostName, hostDirectory, candidate, cancellationToken);

    private static Task<WorkerResponse> InvokeCandidateEnvironmentApiAsync(
        MethodInfo hostMethod,
        string hostName,
        string hostDirectory,
        WorkerCandidatePayload candidate,
        WorkerEnvironmentInput environmentInput,
        CancellationToken cancellationToken)
        => ToolingEntryPoint.InvokeCandidateEnvironmentInspectionAsync(
            hostMethod, hostName, hostDirectory, candidate, environmentInput, cancellationToken);

    private static MethodInfo HostMethod(string name) => typeof(CandidateHost).GetMethod(name,
        BindingFlags.Public | BindingFlags.Static, binder: null,
        types: [typeof(Stream), typeof(Stream), typeof(CancellationToken)], modifiers: null)!;

    private static class CandidateHostState
    {
        private static readonly ConcurrentDictionary<string, CandidateInvocationState> States = new(StringComparer.Ordinal);

        public static void Register(string invocationId) => States[invocationId] = new CandidateInvocationState();
        public static void Remove(string invocationId) => States.TryRemove(invocationId, out _);
        public static CandidateInvocationState For(WorkerCandidatePayload payload) => States[payload.InvocationId!];
        public static CandidateInvocationState For(string invocationId) => States[invocationId];

        public static string SuccessResponse(string invocation, string capture)
        {
            var state = For(invocation);
            var response = CandidateHostResponseFixtures.Success(state.ExpectedCandidate!);
            response["invocationId"] = invocation;
            response["captureId"] = capture;
            state.TransformSuccess?.Invoke(response);
            return response.ToJsonString();
        }

        public static string RefusalResponse(string invocation, string capture) =>
            $"{{\"version\":1,\"invocationId\":\"{invocation}\",\"captureId\":\"{capture}\",\"status\":\"refused\",\"exitCode\":2,\"error\":{{\"code\":\"candidate-capture-invalid\"}}}}";
    }

    private sealed class CandidateInvocationState
    {
        public WorkerCandidatePayload? ExpectedCandidate { get; set; }
        public Action<JsonObject>? TransformSuccess { get; set; }
        public string? LastRequest { get; set; }
        public int InvocationCount { get; set; }
        public Action? CancelCurrent { get; set; }
        public bool OutputLimitBlocked { get; set; }
        public bool RequestWriteDenied { get; set; }
    }

    private static class CandidateHost
    {
        public static Task<int> Success(Stream request, Stream response, CancellationToken cancellationToken) =>
            WriteResponse(request, response, cancellationToken, 0);

        public static async Task<int> EnvironmentSuccess(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var environment = document.RootElement.GetProperty("environmentInput");
            if (environment.GetProperty("version").GetInt32() != 1 ||
                environment.GetProperty("content").ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("private-environment-envelope-canary");

            var candidate = document.RootElement.GetProperty("candidate");
            var invocation = candidate.GetProperty("invocationId").GetString()!;
            var capture = candidate.GetProperty("captureId").GetString()!;
            var json = JsonNode.Parse(CandidateHostState.SuccessResponse(invocation, capture))!.AsObject();
            json["configurationResolution"]!["source"] = "captured-workbench-json-explicit-environment-v1";
            json["configurationResolution"]!["externalInputs"] = "supplied-intended";
            await response.WriteAsync(Encoding.UTF8.GetBytes(json.ToJsonString()), cancellationToken);
            return 0;
        }

        public static Task<int> Refused(Stream request, Stream response, CancellationToken cancellationToken) =>
            WriteResponse(request, response, cancellationToken, 2, refused: true);

        public static Task<int> WrongCorrelation(Stream request, Stream response, CancellationToken cancellationToken) =>
            WriteResponse(request, response, cancellationToken, 0, wrongCorrelation: true);

        public static async Task<int> MalformedResponse(Stream request, Stream response, CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            await response.WriteAsync("private-response-canary"u8.ToArray(), cancellationToken);
            return 0;
        }

        public static Task<int> ExitCodeMismatch(Stream request, Stream response, CancellationToken cancellationToken) =>
            WriteResponse(request, response, cancellationToken, 2);

        public static Task<int> ThrowCanary(Stream request, Stream response, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("private-reflection-canary");

        // Constructed exceptions exercise classification without exhausting memory or corrupting the process.
        public static Task<int> ThrowOutOfMemory(Stream request, Stream response, CancellationToken cancellationToken) =>
            throw new OutOfMemoryException();

        public static Task<int> FaultOutOfMemory(Stream request, Stream response, CancellationToken cancellationToken) =>
            Task.FromException<int>(new OutOfMemoryException());

        public static Task<int> ThrowAccessViolation(Stream request, Stream response, CancellationToken cancellationToken) =>
            throw new AccessViolationException();

        public static Task<int> FaultAccessViolation(Stream request, Stream response, CancellationToken cancellationToken) =>
            Task.FromException<int>(new AccessViolationException());

        public static Task<int> ThrowBadImage(Stream request, Stream response, CancellationToken cancellationToken) =>
            throw new BadImageFormatException("private-image-canary");

        public static Task<int> FaultBadImage(Stream request, Stream response, CancellationToken cancellationToken) =>
            Task.FromException<int>(new BadImageFormatException("private-image-canary"));

        public static async Task<int> CancelDuringCall(Stream request, Stream response, CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            CandidateHostState.For(invocation).CancelCurrent?.Invoke();
            await Task.FromCanceled<int>(cancellationToken);
            return 0;
        }

        public static async Task<int> WriteOversizedResponse(Stream request, Stream response, CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            var state = CandidateHostState.For(invocation);
            var chunk = new byte[1024 * 1024];
            for (var index = 0; index < 4; index++)
                await response.WriteAsync(chunk, cancellationToken);
            try
            {
                await response.WriteAsync(new byte[1], cancellationToken);
            }
            catch
            {
                state.OutputLimitBlocked = true;
                throw;
            }
            return 0;
        }

        public static async Task<int> WriteOversizedResponseAndSwallow(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            var state = CandidateHostState.For(invocation);
            var chunk = new byte[1024 * 1024];
            for (var index = 0; index < 4; index++)
                await response.WriteAsync(chunk, cancellationToken);
            try
            {
                await response.WriteAsync(new byte[1], cancellationToken);
            }
            catch
            {
                state.OutputLimitBlocked = true;
            }
            return 0;
        }

        public static async Task<int> SeekPastResponseLimit(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            try
            {
                response.Seek(4 * 1024 * 1024 + 1, SeekOrigin.Begin);
            }
            catch
            {
                CandidateHostState.For(invocation).OutputLimitBlocked = true;
            }
            return 0;
        }

        public static async Task<int> SetLengthPastResponseLimit(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            try
            {
                response.SetLength(4 * 1024 * 1024 + 1);
            }
            catch
            {
                CandidateHostState.For(invocation).OutputLimitBlocked = true;
            }
            return 0;
        }

        public static async Task<int> AttemptRequestWrite(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            var candidate = document.RootElement.GetProperty("candidate");
            var invocation = candidate.GetProperty("invocationId").GetString()!;
            var capture = candidate.GetProperty("captureId").GetString()!;
            try
            {
                request.WriteByte(0);
            }
            catch (NotSupportedException)
            {
                CandidateHostState.For(invocation).RequestWriteDenied = true;
            }
            await response.WriteAsync(Encoding.UTF8.GetBytes(CandidateHostState.SuccessResponse(invocation, capture)),
                cancellationToken);
            return 0;
        }

        public static async Task<int> DisposeResponse(Stream request, Stream response,
            CancellationToken cancellationToken)
        {
            using var document = await Capture(request);
            response.Dispose();
            return 0;
        }

        private static async Task<int> WriteResponse(Stream request, Stream response, CancellationToken cancellationToken,
            int exitCode, bool refused = false, bool wrongCorrelation = false)
        {
            using var document = await Capture(request);
            var candidate = document.RootElement.GetProperty("candidate");
            var invocation = candidate.GetProperty("invocationId").GetString()!;
            var capture = candidate.GetProperty("captureId").GetString()!;
            var json = refused
                ? CandidateHostState.RefusalResponse(invocation, capture)
                : CandidateHostState.SuccessResponse(invocation, capture);
            if (wrongCorrelation)
            {
                var changed = JsonNode.Parse(json)!;
                changed["invocationId"] = "cccccccccccccccccccccccccccccccc";
                json = changed.ToJsonString();
            }
            await response.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
            return exitCode;
        }

        private static async Task<JsonDocument> Capture(Stream request)
        {
            using var copy = new MemoryStream();
            await request.CopyToAsync(copy);
            var json = Encoding.UTF8.GetString(copy.ToArray());
            using var document = JsonDocument.Parse(json);
            var invocation = document.RootElement.GetProperty("candidate").GetProperty("invocationId").GetString()!;
            var state = CandidateHostState.For(invocation);
            state.InvocationCount++;
            state.LastRequest = json;
            return JsonDocument.Parse(json);
        }
    }

    private const string CandidateEnvironmentInputsAttributeName =
        "Elsa.Persistence.EntityFramework.Tooling.EfCandidateEnvironmentInputsAttribute";
    private const string CandidateEnvironmentInputsPolicy = CandidateInspectionFixture.EnvironmentPolicy;

    private static Assembly CandidateEnvironmentCapabilityAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"CandidateEnvironmentCapability.{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("main");
        var contract = module.DefineType(
            "Elsa.Persistence.EntityFramework.Tooling.EfCandidateEnvironmentInspectionContract",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var version = contract.DefineField("Version", typeof(int),
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
        version.SetConstant(1);
        contract.CreateType();

        var host = module.DefineType(
            "Elsa.Persistence.EntityFramework.Tooling.EfToolingHost",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = host.DefineMethod("RunCandidateEnvironmentInspectionAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<int>), [typeof(Stream), typeof(Stream), typeof(CancellationToken)]);
        var fromResult = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == nameof(Task.FromResult) && candidate.IsGenericMethodDefinition &&
                                 candidate.GetParameters().Length == 1 &&
                                 candidate.GetParameters()[0].ParameterType.IsGenericParameter)
            .MakeGenericMethod(typeof(int));
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);
        host.CreateType();
        return assembly;
    }

    private static Assembly EnrollmentAttributeAssembly(bool exactConstructor = true, bool namedProperty = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"CandidateEnvironmentAttribute.{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType(
            CandidateEnvironmentInputsAttributeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(Attribute));

        var usageConstructor = typeof(AttributeUsageAttribute).GetConstructor([typeof(AttributeTargets)])!;
        type.SetCustomAttribute(new CustomAttributeBuilder(
            usageConstructor,
            [AttributeTargets.Assembly],
            [typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))!,
             typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.Inherited))!],
            [true, false]));

        var constructorParameters = exactConstructor ? new[] { typeof(int), typeof(string) } : new[] { typeof(int) };
        var constructor = type.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            constructorParameters);
        var constructorIl = constructor.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        constructorIl.Emit(OpCodes.Ldstr, "candidate-environment-attribute-constructor-must-not-run");
        constructorIl.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        constructorIl.Emit(OpCodes.Throw);

        DefineProperty(type, "Version", typeof(int));
        DefineProperty(type, "Policy", typeof(string));
        if (namedProperty)
            DefineProperty(type, "Marker", typeof(string), writable: true);

        return type.CreateType()!.Assembly;
    }

    private static Assembly HostAssemblyWithEnrollment(
        Assembly attributeAssembly,
        int version = 1,
        string policy = CandidateEnvironmentInputsPolicy,
        int declarations = 1,
        bool namedArgument = false)
    {
        var attributeType = attributeAssembly.GetType(CandidateEnvironmentInputsAttributeName, throwOnError: true)!;
        var constructor = attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var arguments = constructor.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(string) ? (object)policy : version).ToArray();
        var namedProperties = namedArgument
            ? new[] { attributeType.GetProperty("Marker", BindingFlags.Public | BindingFlags.Instance)! }
            : Array.Empty<PropertyInfo>();
        var namedValues = namedArgument ? new object[] { "named" } : Array.Empty<object>();
        var host = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"CandidateEnvironmentHost.{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);

        for (var index = 0; index < declarations; index++)
            host.SetCustomAttribute(new CustomAttributeBuilder(constructor, arguments, namedProperties, namedValues));

        return host.DefineDynamicModule("main").DefineType("HostMarker").CreateType()!.Assembly;
    }

    private static Assembly EmptyMetadataAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"CandidateEnvironmentEmptyHost.{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        return assembly.DefineDynamicModule("main").DefineType("HostMarker").CreateType()!.Assembly;
    }

    private static void DefineProperty(TypeBuilder type, string name, Type propertyType, bool writable = false)
    {
        var field = type.DefineField($"_{name}", propertyType, FieldAttributes.Private);
        var getter = type.DefineMethod($"get_{name}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            propertyType, Type.EmptyTypes);
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, field);
        getterIl.Emit(OpCodes.Ret);
        var property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
        property.SetGetMethod(getter);
        if (!writable)
            return;

        var setter = type.DefineMethod($"set_{name}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            typeof(void), [propertyType]);
        var setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, field);
        setterIl.Emit(OpCodes.Ret);
        property.SetSetMethod(setter);
    }

    private static class CurrentCandidateProtocol { public const int Version = 1; }
    private static class CurrentCandidateEnvironmentProtocol { public const int Version = 1; }
    private static class UnknownCandidateEnvironmentProtocol { public const int Version = 99; }
    private static class MutableCandidateEnvironmentProtocol { public static readonly int Version = 1; }
    private static class UnknownCandidateProtocol { public const int Version = 99; }
    private static class WrongTypeCandidateProtocol { public const string Version = "1"; }
    private static class MutableCandidateProtocol { public static readonly int Version = 1; }
    private static class CompleteCandidateHost
    {
        public static Task<int> RunCandidateInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private static class CompleteCandidateEnvironmentHost
    {
        public static Task<int> RunCandidateEnvironmentInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private static class WrongReturnCandidateHost
    {
        public static Task<string> RunCandidateInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult("private-2177");
    }
    private static class WrongSignatureCandidateHost
    {
        public static Task<int> RunCandidateInspectionAsync(Stream request, Stream response) => Task.FromResult(0);
    }
    private static class GenericCandidateHost
    {
        public static Task<int> RunCandidateInspectionAsync<T>(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private static class WrongReturnCandidateEnvironmentHost
    {
        public static Task<string> RunCandidateEnvironmentInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult("private-2292");
    }
    private static class WrongSignatureCandidateEnvironmentHost
    {
        public static Task<int> RunCandidateEnvironmentInspectionAsync(Stream request, Stream response) => Task.FromResult(0);
    }
    private static class GenericCandidateEnvironmentHost
    {
        public static Task<int> RunCandidateEnvironmentInspectionAsync<T>(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private sealed class InstanceCandidateHost
    {
        public Task<int> RunCandidateInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
    private sealed class InstanceCandidateEnvironmentHost
    {
        public Task<int> RunCandidateEnvironmentInspectionAsync(Stream request, Stream response,
            CancellationToken cancellationToken) => Task.FromResult(0);
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

    private sealed class ThrowingDisposeContext(string message) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException(message);
        }
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
