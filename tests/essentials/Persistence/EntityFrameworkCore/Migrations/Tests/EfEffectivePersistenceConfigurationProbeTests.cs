using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.Configuration;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cli;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Modularity.EntityFramework;
using Elsa.Modularity.Planning.Catalog;
using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Exercises effective persistence selection through the real shell-settings and host-list boundaries.
/// These cases inspect configuration only; they never activate a feature or open a database.
/// </summary>
public sealed class EfEffectivePersistenceConfigurationProbeTests : IDisposable
{
    private const string Shell = "persistence-probe";
    private const string Runtime = "WorkflowsRuntimeEntityFrameworkCore";
    private const string RuntimeWorkflowExecution = "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence";
    private const string StructuredLogs = "DiagnosticsStructuredLogsEntityFrameworkCore";
    private const string OpenTelemetry = "DiagnosticsOpenTelemetryEntityFrameworkCore";
    private const string ConnectionCanary = "connection-value-canary-2172";
    private const string DifferentConnectionCanary = "different-target-canary-2172";
    private const string UnknownSettingCanary = "raw-unknown-setting-canary-2172";
    private const string InlineIdentityCanary = "Data Source=fixture.db;Password=inline-reference-canary-2172";

    private static readonly string[] DiagnosticFeatureIds = [StructuredLogs, OpenTelemetry];

    private static readonly Assembly[] HostAssemblies = new[]
    {
        typeof(EfToolingHostTests).Assembly,
        typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
        typeof(StructuredLogsEntityFrameworkCoreFeature).Assembly,
        typeof(EfOpenTelemetryFeature).Assembly,
        typeof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsRuntimeResumptionFeature).Assembly,
        typeof(TasksFeature).Assembly,
        typeof(StructuredLogsFeature).Assembly,
        typeof(OpenTelemetryFeature).Assembly
    }.Concat(ModuleContextCatalog.Modules).Distinct().ToArray();

    private readonly string temporaryDirectory = Directory.CreateTempSubdirectory("elsa-effective-persistence-probe-").FullName;

    public void Dispose() => Directory.Delete(temporaryDirectory, recursive: true);

    [Fact]
    public async Task Runtime_patch_and_v2_list_agree_on_root_default_and_environment_layered_diagnostic_bindings()
    {
        var configuration = BuildConfiguration(new ProbeOptions());
        var run = await ProbeAsync(configuration, Shell);

        Assert.Null(run.RuntimeFailure);
        Assert.Empty(run.RuntimeDetails.RefusalCodes);
        Assert.Equal(6, run.RuntimePatch!.ConfigurationData.Count);
        Assert.Contains("WorkflowsRuntimeResumption", run.RuntimeContext.ImplicitFeatureIds);
        Assert.Contains("Tasks", run.RuntimeContext.ImplicitFeatureIds);
        Assert.Contains("DiagnosticsStructuredLogs", run.RuntimeContext.ImplicitFeatureIds);
        Assert.Contains("DiagnosticsOpenTelemetry", run.RuntimeContext.ImplicitFeatureIds);

        AssertRuntimeParticipant(run, Runtime, "primary", "Sqlite", "PrimaryProduction", "RootDefault", "root");
        AssertRuntimeParticipant(run, StructuredLogs, "logs-production", "Sqlite", "DiagnosticsLogsShared", "ShellBinding", "shell-composed");
        AssertRuntimeParticipant(run, OpenTelemetry, "telemetry-production", "Sqlite", "DiagnosticsTelemetryShared", "ShellBinding", "shell-composed");

        var response = SuccessfulResponse(run);
        var facts = response.GetProperty("configurationContext");
        Assert.Equal("Production", facts.GetProperty("environment").GetString());
        Assert.Equal(Shell, facts.GetProperty("shell").GetString());
        Assert.Equal("not-performed", facts.GetProperty("targetVerification").GetString());
        Assert.Equal("unobserved", facts.GetProperty("runtimeParity").GetString());
        Assert.Equal(["expected-connection-unchecked", "target-affinity-unverified"], Unresolved(facts));
        AssertToolingParticipant(facts, Runtime, "Workflows.Runtime", "primary", "Sqlite", "PrimaryProduction", "RootDefault");
        AssertToolingParticipant(facts, StructuredLogs, "Diagnostics.StructuredLogs", "logs-production", "Sqlite", "DiagnosticsLogsShared", "ShellBinding");
        AssertToolingParticipant(facts, OpenTelemetry, "Diagnostics.OpenTelemetry", "telemetry-production", "Sqlite", "DiagnosticsTelemetryShared", "ShellBinding");

        Assert.Equal(ParticipantFields, facts.GetProperty("participants").EnumerateArray().First()
            .EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Shell_default_overrides_root_default_while_explicit_diagnostic_bindings_override_the_shell_default()
    {
        var configuration = BuildConfiguration(new ProbeOptions { IncludeShellDefault = true });
        var run = await ProbeAsync(configuration, Shell);

        Assert.Null(run.RuntimeFailure);
        Assert.Empty(run.RuntimeDetails.RefusalCodes);
        AssertRuntimeParticipant(run, Runtime, "shell-production", "Sqlite", "ShellProduction", "ShellDefault", "shell-composed");
        AssertRuntimeParticipant(run, StructuredLogs, "logs-production", "Sqlite", "DiagnosticsLogsShared", "ShellBinding", "shell-composed");
        AssertRuntimeParticipant(run, OpenTelemetry, "telemetry-production", "Sqlite", "DiagnosticsTelemetryShared", "ShellBinding", "shell-composed");

        var facts = SuccessfulResponse(run).GetProperty("configurationContext");
        AssertToolingParticipant(facts, Runtime, "Workflows.Runtime", "shell-production", "Sqlite", "ShellProduction", "ShellDefault");
        AssertToolingParticipant(facts, StructuredLogs, "Diagnostics.StructuredLogs", "logs-production", "Sqlite", "DiagnosticsLogsShared", "ShellBinding");
        AssertToolingParticipant(facts, OpenTelemetry, "Diagnostics.OpenTelemetry", "telemetry-production", "Sqlite", "DiagnosticsTelemetryShared", "ShellBinding");

        // The existing adapter reports generic scopes; neither identifies a source filename.
        var runtime = Resolution(run.RuntimeDetails, Runtime);
        Assert.Equal("root", run.RuntimeDetails.ResourceDefinitions[runtime.ResourceName!].Provider.Source.Scope);
        Assert.Equal("configuration", runtime.Source.Mode);
        Assert.DoesNotContain("resourceDefinitionSource", ParticipantProperties(facts), StringComparer.Ordinal);
        Assert.DoesNotContain("settingsProvenance", ParticipantProperties(facts), StringComparer.Ordinal);
    }

    [Fact]
    public async Task V2_host_list_stays_host_scoped_when_accepted_selection_removes_open_telemetry()
    {
        var catalog = FoundationSelectionCatalog.Load();
        var acceptedCandidate = SelectionPlanner.Plan(catalog, new AuthoredComposition(
            "1",
            new CatalogPin(catalog.Id, catalog.Version, catalog.Digest),
            null,
            [],
            [Runtime, StructuredLogs],
            [OpenTelemetry],
            new AcceptedSelection(catalog.Digest, [Runtime, StructuredLogs], []),
            null,
            null));
        Assert.Contains(StructuredLogs, acceptedCandidate.SelectedFeatureIds);
        Assert.DoesNotContain(OpenTelemetry, acceptedCandidate.SelectedFeatureIds);
        Assert.Equal(acceptedCandidate.Accepted.FeatureIds.ToArray(), acceptedCandidate.SelectedFeatureIds.ToArray());
        Assert.DoesNotContain(acceptedCandidate.Findings, finding => finding.Code == "candidate-re-resolution");

        // The accepted set keeps Runtime and Structured Logs but removes OpenTelemetry, which is
        // enabled in the host. The v2 operation receives only a host module selection, not those IDs.
        var hostRun = await ProbeAsync(BuildConfiguration(new ProbeOptions()), Shell);
        var facts = SuccessfulResponse(hostRun).GetProperty("configurationContext");
        AssertToolingParticipant(facts, Runtime, "Workflows.Runtime", "primary", "Sqlite", "PrimaryProduction", "RootDefault");
        AssertToolingParticipant(facts, StructuredLogs, "Diagnostics.StructuredLogs", "logs-production", "Sqlite", "DiagnosticsLogsShared", "ShellBinding");
        AssertToolingParticipant(facts, OpenTelemetry, "Diagnostics.OpenTelemetry", "telemetry-production", "Sqlite", "DiagnosticsTelemetryShared", "ShellBinding");
        Assert.DoesNotContain(OpenTelemetry, acceptedCandidate.SelectedFeatureIds);
        Assert.Contains(facts.GetProperty("participants").EnumerateArray(), participant =>
            participant.GetProperty("feature").GetString() == OpenTelemetry);
    }

    [Fact]
    public async Task Tooling_marks_distinct_diagnostic_references_unverified_while_runtime_preparation_checks_configured_connections()
    {
        var sameReference = await ProbeAsync(
            BuildConfiguration(new ProbeOptions { SameDiagnosticsReference = true }), Shell);
        Assert.Null(sameReference.RuntimeFailure);
        Assert.Empty(sameReference.RuntimeDetails.RefusalCodes);
        Assert.Equal(["expected-connection-unchecked"], Unresolved(SuccessfulResponse(sameReference)
            .GetProperty("configurationContext")));

        var differentValues = await ProbeAsync(
            BuildConfiguration(new ProbeOptions { DifferentDiagnosticConnectionValues = true }), Shell);
        Assert.Equal("EF persistence preparation refused: resource-context-conflict", differentValues.RuntimeFailure);
        Assert.Equal(["resource-context-conflict"], differentValues.RuntimeDetails.RefusalCodes);
        var offlineFacts = SuccessfulResponse(differentValues).GetProperty("configurationContext");
        Assert.Equal(["expected-connection-unchecked", "target-affinity-unverified"], Unresolved(offlineFacts));
    }

    [Fact]
    public async Task All_legacy_is_a_control_and_missing_resource_binding_or_mixed_diagnostics_return_stable_codes()
    {
        var legacy = await ProbeAsync(BuildConfiguration(new ProbeOptions
        {
            IncludeRootDefault = false,
            AllLegacy = true
        }), Shell);
        Assert.Null(legacy.RuntimeFailure);
        Assert.False(legacy.RuntimeDetails.HasApplicableResource);
        Assert.Empty(legacy.RuntimePatch!.ConfigurationData);
        Assert.All(legacy.RuntimeDetails.ResolvedParticipants, item => Assert.Equal("Legacy", item.Selection.ToString()));
        var legacyFacts = SuccessfulResponse(legacy).GetProperty("configurationContext");
        Assert.Equal("legacy", legacyFacts.GetProperty("resolution").GetString());
        Assert.All(legacyFacts.GetProperty("participants").EnumerateArray(), participant =>
            Assert.Equal("Legacy", participant.GetProperty("selection").GetString()));
        Assert.Empty(Unresolved(legacyFacts));

        var missing = await ProbeAsync(BuildConfiguration(new ProbeOptions { RootDefaultResource = "missing-resource" }), Shell);
        Assert.Equal("EF persistence preparation refused: resource-not-found", missing.RuntimeFailure);
        Assert.Equal(["resource-not-found"], missing.RuntimeDetails.RefusalCodes);
        Assert.Equal("resource-not-found", ErrorCode(missing));

        var missingBinding = await ProbeAsync(BuildConfiguration(new ProbeOptions { MissingBindingResource = true }), Shell);
        Assert.Equal("EF persistence preparation refused: resource-not-found", missingBinding.RuntimeFailure);
        Assert.Equal(["resource-not-found"], missingBinding.RuntimeDetails.RefusalCodes);
        Assert.Equal("resource-not-found", ErrorCode(missingBinding));

        var mixed = await ProbeAsync(BuildConfiguration(new ProbeOptions
        {
            IncludeRootDefault = false,
            MixedDiagnostics = true
        }), Shell);
        Assert.Equal("EF persistence preparation refused: resource-ownership-unresolved", mixed.RuntimeFailure);
        Assert.Equal(["resource-ownership-unresolved"], mixed.RuntimeDetails.RefusalCodes);
        Assert.Equal("resource-ownership-unresolved", ErrorCode(mixed));
    }

    [Theory]
    [InlineData("resource", "resource-selection-invalid")]
    [InlineData("connection-name", "resource-definition-invalid")]
    public async Task Runtime_and_v2_refuse_inline_connection_syntax_in_logical_identity_fields_without_echoing_it(
        string inlineField,
        string refusalCode)
    {
        var run = await ProbeAsync(BuildConfiguration(new ProbeOptions { InlineIdentityField = inlineField }), Shell);
        Assert.Equal($"EF persistence preparation refused: {refusalCode}", run.RuntimeFailure);
        Assert.Equal([refusalCode], run.RuntimeDetails.RefusalCodes);
        Assert.Equal(refusalCode, ErrorCode(run));

        // The selected identity field deliberately contains invalid inline-connection syntax,
        // with no colon so it stays one JSON/configuration key. Inspect only a private boolean.
        var refusalDiagnosticsEchoInlineIdentity =
            (run.RuntimeFailure?.Contains(InlineIdentityCanary, StringComparison.Ordinal) ?? false) ||
            run.ResponseJson.Contains(InlineIdentityCanary, StringComparison.Ordinal);
        Assert.False(refusalDiagnosticsEchoInlineIdentity,
            "Stable Runtime and v2 refusal diagnostics must not echo non-reference-like identity content.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Features_sharing_a_runtime_context_refuse_provider_or_schema_splits(bool providerMismatch, bool schemaMismatch)
    {
        var run = await ProbeAsync(BuildConfiguration(new ProbeOptions
        {
            RuntimePair = true,
            ProviderMismatch = providerMismatch,
            SchemaMismatch = schemaMismatch
        }), Shell);

        Assert.Equal("EF persistence preparation refused: resource-context-conflict", run.RuntimeFailure);
        Assert.Equal(["resource-context-conflict"], run.RuntimeDetails.RefusalCodes);
        Assert.Equal("resource-context-conflict", ErrorCode(run));
    }

    [Fact]
    public void Composition_source_and_tooling_file_capture_can_observe_different_environment_overlay_bytes()
    {
        WriteCaptureFiles(temporaryDirectory);
        var source = CompositionFileSource.Open(temporaryDirectory, Shell, "Production");
        using var capturedOverlay = JsonDocument.Parse(source.Snapshot.ReadText("appsettings.Production.json"));
        var sourceSnapshotRetainsOriginalTarget = capturedOverlay.RootElement.GetProperty("Elsa")
            .GetProperty("Persistence").GetProperty("DefaultResource").GetString() == "captured-target";
        Assert.True(sourceSnapshotRetainsOriginalTarget,
            "The CompositionFileSource snapshot should retain the target from its captured overlay.");

        WriteApplicationEnvironmentFile(temporaryDirectory, "changed-target");

        // This temporary host cannot satisfy v2's loaded-host assembly path check. The separate
        // capture case deliberately stops at the production tooling reader's shell preparation.
        using var context = EfToolingConfigurationContext.Create(
            EfToolingConfigurationContext.WorkbenchJson,
            temporaryDirectory,
            typeof(EfToolingHostTests).Assembly.GetName().Name!,
            "Production",
            Shell,
            explicitSelection: true,
            CancellationToken.None);
        var prepared = context.PrepareShell(new EfToolingHostTestDefaults(), HostAssemblies, CancellationToken.None);

        Assert.Empty(prepared.RefusalCodes);
        var runtime = Assert.Single(prepared.ResolvedParticipants, item => item.Participant.FeatureId == Runtime);
        Assert.Equal("changed-target", runtime.ResourceName);
        var changed = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", changed.Code);
        AssertRawValuesAbsent(null, changed.ToString(), string.Empty);
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory, "*.db"));
        Assert.Equal(
            ["appsettings.Production.json", "appsettings.json", "shells.Production.json", "shells.json"],
            Directory.EnumerateFiles(temporaryDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private async Task<ProbeRun> ProbeAsync(IConfigurationRoot configuration, string shell)
    {
        var runtimeContext = ComposeRuntimeContext(configuration, shell);
        ShellSettingsPreparationResult? patch = null;
        string? runtimeFailure = null;
        try
        {
            patch = await new EfPersistenceShellSettingsPreparer(configuration)
                .PrepareAsync(runtimeContext, CancellationToken.None);
        }
        catch (InvalidOperationException failure)
        {
            runtimeFailure = failure.Message;
        }

        // The actual runtime preparer above always runs. This shared metadata result supplies the
        // detached identities that the public runtime patch intentionally does not expose.
        var details = EfPersistencePreparation.Prepare(runtimeContext, configuration, HostAssemblies,
            verifyConnectionValues: true);
        using var toolingContext = ToolingContextForTestAssembly(configuration, shell);
        using var request = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"version":2,"command":"list","selection":{"kind":"from-host"}}"""));
        using var response = new MemoryStream();
        var exitCode = await EfToolingContextOperation.RunAsync(
            request, response, toolingContext, HostAssemblies, CancellationToken.None);
        var responseJson = Encoding.UTF8.GetString(response.ToArray());
        AssertRawValuesAbsent(patch, runtimeFailure, responseJson);
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory, "*.db"));
        using var document = JsonDocument.Parse(responseJson);

        return new ProbeRun(runtimeContext, patch, runtimeFailure, details, exitCode,
            responseJson, document.RootElement.Clone());
    }

    private static ShellSettingsPreparationContext ComposeRuntimeContext(IConfiguration configuration, string shell)
    {
        var descriptors = FeatureDiscovery.DiscoverFeatures(HostAssemblies)
            .ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var builder = new ShellBuilder(shell);
        new EfToolingHostTestDefaults().Configure(builder, configuration);
        builder.FromConfiguration(configuration.GetSection($"CShells:Shells:{shell}"));
        var settings = builder.Build();
        var requested = settings.EnabledFeatures.ToArray();
        var orderedIds = new FeatureDependencyResolver().GetOrderedFeatures(
            requested.Where(descriptors.ContainsKey), descriptors);
        var ordered = orderedIds.Select(id =>
        {
            var feature = descriptors[id];
            return new ShellFeaturePreparationDescriptor(
                id, feature.Dependencies, feature.StartupType,
                settings.FeatureConfigurators.ContainsKey(id));
        }).ToArray();

        return new ShellSettingsPreparationContext(
            settings.Id,
            settings.ConfigurationData.ToDictionary(entry => entry.Key, entry => entry.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase),
            orderedIds,
            settings.DisabledFeatures,
            settings.FeatureSettingResets,
            ordered,
            requested,
            orderedIds.Except(requested, StringComparer.OrdinalIgnoreCase).ToArray(),
            requested.Where(id => !descriptors.ContainsKey(id)).ToArray());
    }

    private static EfToolingConfigurationContext ToolingContextForTestAssembly(IConfigurationRoot configuration, string shell)
    {
        var assembly = typeof(EfToolingHostTests).Assembly;
        return new EfToolingConfigurationContext(EfToolingConfigurationContext.WorkbenchJson,
            Path.GetDirectoryName(assembly.Location)!, assembly.GetName().Name!, "Production", shell,
            explicitSelection: true, configuration);
    }

    private static IConfigurationRoot BuildConfiguration(ProbeOptions options)
    {
        var layers = new[]
        {
            ApplicationSettings(options),
            ApplicationEnvironmentSettings(options),
            ShellSettings(options),
            ShellEnvironmentSettings(options)
        };
        var streams = layers.Select(layer => new MemoryStream(Encoding.UTF8.GetBytes(layer.ToJsonString()))).ToArray();
        try
        {
            var builder = new ConfigurationBuilder();
            foreach (var stream in streams)
                builder.AddJsonStream(stream);
            return (IConfigurationRoot)builder.Build();
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }
    }

    private static JsonObject ApplicationSettings(ProbeOptions options)
    {
        var resources = new JsonObject();
        var connectionStrings = new JsonObject();
        if (options.RuntimePair)
        {
            resources["primary"] = Resource(options.ProviderMismatch ? "Sqlite" : "PostgreSql", "RuntimeShared");
            resources["runtime-other"] = Resource("PostgreSql", "RuntimeShared");
            connectionStrings["RuntimeShared"] = $"Data Source=:memory:;Password={ConnectionCanary}";
        }
        else
        {
            resources["root-base"] = Resource("Sqlite", "RootBase");
            resources["primary"] = Resource("Sqlite", "PrimaryBase");
            resources["shell-base"] = Resource("Sqlite", "ShellBase");
            resources["logs-base"] = Resource("Sqlite", "DiagnosticsLogsBase");
            resources["telemetry-base"] = Resource("Sqlite", "DiagnosticsTelemetryBase");
            connectionStrings["RootBase"] = ConnectionCanary;
            connectionStrings["PrimaryBase"] = ConnectionCanary;
            connectionStrings["ShellBase"] = ConnectionCanary;
            connectionStrings["DiagnosticsLogsBase"] = ConnectionCanary;
            connectionStrings["DiagnosticsTelemetryBase"] = ConnectionCanary;
        }

        var persistence = new JsonObject
        {
            ["Resources"] = resources,
            ["UnmodeledSetting"] = UnknownSettingCanary
        };
        if (options.IncludeRootDefault)
            persistence["DefaultResource"] = options.RuntimePair ? "primary" : "root-base";

        return new JsonObject
        {
            ["ProbeDefaults"] = new JsonObject { ["AddRuntimeEfFeature"] = true },
            ["Elsa"] = new JsonObject { ["Persistence"] = persistence },
            ["ConnectionStrings"] = connectionStrings
        };
    }

    private static JsonObject ApplicationEnvironmentSettings(ProbeOptions options)
    {
        var resources = new JsonObject();
        var connectionStrings = new JsonObject();
        if (options.RuntimePair)
        {
            resources["runtime-other"] = Resource("PostgreSql", "RuntimeShared");
            connectionStrings["RuntimeShared"] = $"Data Source=:memory:;Password={ConnectionCanary}";
        }
        else
        {
            var resourceName = options.InlineIdentityField == "resource" ? InlineIdentityCanary : "primary";
            var connectionName = options.InlineIdentityField == "connection-name" ? InlineIdentityCanary : "PrimaryProduction";
            resources[resourceName] = Resource("Sqlite", connectionName);
            resources["shell-production"] = Resource("Sqlite", "ShellProduction");
            resources["logs-production"] = Resource("Sqlite", "DiagnosticsLogsShared");
            resources["telemetry-production"] = Resource("Sqlite", "DiagnosticsTelemetryShared");
            connectionStrings["PrimaryProduction"] = $"Data Source=:memory:;Password={ConnectionCanary}";
            if (options.InlineIdentityField == "connection-name")
                connectionStrings[InlineIdentityCanary] = $"Data Source=:memory:;Password={ConnectionCanary}";
            connectionStrings["ShellProduction"] = $"Data Source=:memory:;Password={ConnectionCanary}";
            connectionStrings["DiagnosticsLogsShared"] = $"Data Source=:memory:;Password={ConnectionCanary}";
            connectionStrings["DiagnosticsTelemetryShared"] = options.DifferentDiagnosticConnectionValues
                ? $"Data Source=:memory:;Password={DifferentConnectionCanary}"
                : $"Data Source=:memory:;Password={ConnectionCanary}";
        }

        var persistence = new JsonObject { ["Resources"] = resources };
        if (options.IncludeRootDefault)
            persistence["DefaultResource"] = options.InlineIdentityField == "resource"
                ? InlineIdentityCanary
                : options.RootDefaultResource;

        return new JsonObject
        {
            ["Elsa"] = new JsonObject { ["Persistence"] = persistence },
            ["ConnectionStrings"] = connectionStrings
        };
    }

    private static JsonObject ShellSettings(ProbeOptions options)
    {
        var features = new JsonObject
        {
            [StructuredLogs] = Feature(),
            [OpenTelemetry] = Feature()
        };
        if (options.AllLegacy)
        {
            features[Runtime] = LegacyFeature("LegacyRuntime");
            features[StructuredLogs] = LegacyFeature("LegacyLogs");
            features[OpenTelemetry] = LegacyFeature("LegacyTelemetry");
        }
        else if (options.MixedDiagnostics)
        {
            features[OpenTelemetry] = LegacyFeature("LegacyTelemetry");
        }
        else if (options.RuntimePair)
        {
            features[Runtime] = Feature(schema: options.SchemaMismatch ? "runtime-schema-a" : null);
            features[RuntimeWorkflowExecution] = Feature(schema: options.SchemaMismatch ? "runtime-schema-b" : null);
        }

        var persistence = new JsonObject();
        if (options.IncludeShellDefault)
            persistence["DefaultResource"] = "shell-base";
        if (options.AllLegacy)
        {
            // Legacy feature settings are deliberately present to prove they remain outside the resource patch.
        }
        else if (options.MixedDiagnostics)
        {
            persistence["Bindings"] = new JsonObject { [StructuredLogs] = "logs-base" };
        }
        else if (options.RuntimePair)
        {
            persistence["Bindings"] = new JsonObject { [RuntimeWorkflowExecution] = "runtime-other" };
        }
        else
        {
            persistence["Bindings"] = DiagnosticBindings(options.SameDiagnosticsReference, production: false);
        }

        return new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [options.ShellName] = new JsonObject
                    {
                        ["Name"] = options.ShellName,
                        ["Features"] = features,
                        ["Configuration"] = new JsonObject
                        {
                            ["Elsa"] = new JsonObject { ["Persistence"] = persistence }
                        }
                    }
                }
            }
        };
    }

    private static JsonObject ShellEnvironmentSettings(ProbeOptions options)
    {
        var persistence = new JsonObject();
        if (options.IncludeShellDefault)
            persistence["DefaultResource"] = "shell-production";
        if (options.MixedDiagnostics)
            persistence["Bindings"] = new JsonObject { [StructuredLogs] = "logs-production" };
        else if (options.RuntimePair)
            persistence["Bindings"] = new JsonObject { [RuntimeWorkflowExecution] = "runtime-other" };
        else if (!options.AllLegacy && options.IncludeRootDefault)
        {
            var bindings = DiagnosticBindings(options.SameDiagnosticsReference, production: true);
            if (options.MissingBindingResource)
                bindings[StructuredLogs] = "missing-binding-target";
            persistence["Bindings"] = bindings;
        }

        return new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [options.ShellName] = new JsonObject
                    {
                        ["Configuration"] = new JsonObject
                        {
                            ["Elsa"] = new JsonObject { ["Persistence"] = persistence }
                        }
                    }
                }
            }
        };
    }

    private static JsonObject DiagnosticBindings(bool sameReference, bool production) => sameReference
        ? new JsonObject
        {
            [StructuredLogs] = production ? "logs-production" : "logs-base",
            [OpenTelemetry] = production ? "logs-production" : "logs-base"
        }
        : new JsonObject
        {
            [StructuredLogs] = production ? "logs-production" : "logs-base",
            [OpenTelemetry] = production ? "telemetry-production" : "telemetry-base"
        };

    private static JsonObject Resource(string provider, string connectionName) => new()
    {
        ["Provider"] = provider,
        ["ConnectionName"] = connectionName
    };

    private static JsonObject Feature(string? provider = null, string? connectionName = null, string? schema = null)
    {
        var settings = new JsonObject { ["UnknownProbeSetting"] = UnknownSettingCanary };
        if (provider is not null)
            settings["Provider"] = provider;
        if (connectionName is not null)
            settings["ConnectionName"] = connectionName;
        if (schema is not null)
            settings["Schema"] = schema;
        return settings;
    }

    private static JsonObject LegacyFeature(string connectionName) => new()
    {
        ["Provider"] = "Sqlite",
        ["ConnectionName"] = connectionName,
        ["ConnectionString"] = $"Data Source=:memory:;Password={ConnectionCanary}",
        ["UnknownProbeSetting"] = UnknownSettingCanary
    };

    private static void AssertRuntimeParticipant(
        ProbeRun run,
        string feature,
        string resource,
        string provider,
        string connectionName,
        string selection,
        string scope)
    {
        var result = run.RuntimeDetails;
        var item = Assert.Single(result.Participants, participant => participant.FeatureId == feature);
        Assert.Equal(resource, item.ResourceName);
        Assert.Equal(selection, item.SelectionKind);
        var resolution = Resolution(result, feature);
        Assert.Equal(provider, resolution.Provider);
        Assert.Equal(connectionName, resolution.ConnectionName);
        Assert.Equal(scope, resolution.Source.Scope);
        Assert.Equal(selection, resolution.Selection.ToString());
        Assert.Equal(provider, run.RuntimePatch!.ConfigurationData[$"{feature}:Provider"]);
        Assert.Equal(connectionName, run.RuntimePatch.ConfigurationData[$"{feature}:ConnectionName"]);
    }

    private static PersistenceParticipantResolution Resolution(EfPersistencePreparationResult result, string feature) =>
        Assert.Single(result.ResolvedParticipants, participant => participant.Participant.FeatureId == feature);

    private static void AssertToolingParticipant(
        JsonElement facts,
        string feature,
        string module,
        string resource,
        string provider,
        string connectionReference,
        string selection)
    {
        var participant = Assert.Single(facts.GetProperty("participants").EnumerateArray(),
            item => item.GetProperty("feature").GetString() == feature);
        Assert.Equal(module, participant.GetProperty("module").GetString());
        Assert.Equal(resource, participant.GetProperty("resource").GetString());
        Assert.Equal(provider, participant.GetProperty("provider").GetString());
        Assert.Equal(connectionReference, participant.GetProperty("connectionReference").GetString());
        Assert.Equal(selection, participant.GetProperty("selection").GetString());
    }

    private static JsonElement SuccessfulResponse(ProbeRun run)
    {
        Assert.Equal(EfToolingExitCode.Success, run.ToolingExitCode);
        Assert.Equal("ok", run.Response.GetProperty("status").GetString());
        return run.Response;
    }

    private static string ErrorCode(ProbeRun run)
    {
        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ToolingExitCode);
        return run.Response.GetProperty("error").GetProperty("code").GetString()!;
    }

    private static string[] Unresolved(JsonElement facts) => [.. facts.GetProperty("unresolved")
        .EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];

    private static void AssertRawValuesAbsent(ShellSettingsPreparationResult? patch, string? failure, string response)
    {
        var serializedPatch = patch is null ? string.Empty : JsonSerializer.Serialize(patch);
        foreach (var rawCanary in new[] { ConnectionCanary, DifferentConnectionCanary, UnknownSettingCanary })
        {
            Assert.False(serializedPatch.Contains(rawCanary, StringComparison.Ordinal),
                "A serialized public patch exposed a raw connection or unknown-setting value.");
            Assert.False(failure?.Contains(rawCanary, StringComparison.Ordinal) ?? false,
                "A preparation diagnostic exposed a raw connection or unknown-setting value.");
            Assert.False(response.Contains(rawCanary, StringComparison.Ordinal),
                "A v2 response exposed a raw connection or unknown-setting value.");
        }
    }

    private static IEnumerable<string> ParticipantProperties(JsonElement facts) => facts.GetProperty("participants")
        .EnumerateArray().SelectMany(participant => participant.EnumerateObject().Select(property => property.Name));

    private static readonly string[] ParticipantFields =
        ["feature", "module", "resource", "provider", "connectionReference", "selection"];

    private void WriteCaptureFiles(string directory)
    {
        WriteJson(Path.Join(directory, "appsettings.json"), new JsonObject
        {
            ["ProbeDefaults"] = new JsonObject { ["AddRuntimeEfFeature"] = true },
            ["Elsa"] = new JsonObject
            {
                ["Persistence"] = new JsonObject
                {
                    ["DefaultResource"] = "base-target",
                    ["Resources"] = new JsonObject { ["base-target"] = Resource("Sqlite", "Base") }
                }
            },
            ["ConnectionStrings"] = new JsonObject { ["Base"] = $"Data Source=:memory:;Password={ConnectionCanary}" }
        });
        WriteApplicationEnvironmentFile(directory, "captured-target");
        WriteJson(Path.Join(directory, "shells.json"), new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    [Shell] = new JsonObject
                    {
                        ["Name"] = Shell,
                        ["Features"] = new JsonObject(),
                        ["Configuration"] = new JsonObject()
                    }
                }
            }
        });
        WriteJson(Path.Join(directory, "shells.Production.json"), new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject { [Shell] = new JsonObject { ["Configuration"] = new JsonObject() } }
            }
        });
    }

    private static void WriteApplicationEnvironmentFile(string directory, string selectedResource)
    {
        WriteJson(Path.Join(directory, "appsettings.Production.json"), new JsonObject
        {
            ["Elsa"] = new JsonObject
            {
                ["Persistence"] = new JsonObject
                {
                    ["DefaultResource"] = selectedResource,
                    ["Resources"] = new JsonObject { [selectedResource] = Resource("Sqlite", "Environment") }
                }
            },
            ["ConnectionStrings"] = new JsonObject
            {
                ["Environment"] = $"Data Source=:memory:;Password={ConnectionCanary}"
            }
        });
    }

    private static void WriteJson(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString());

    private sealed record ProbeRun(
        ShellSettingsPreparationContext RuntimeContext,
        ShellSettingsPreparationResult? RuntimePatch,
        string? RuntimeFailure,
        EfPersistencePreparationResult RuntimeDetails,
        int ToolingExitCode,
        string ResponseJson,
        JsonElement Response);

    private sealed record ProbeOptions
    {
        public string ShellName { get; init; } = Shell;
        public bool IncludeRootDefault { get; init; } = true;
        public string RootDefaultResource { get; init; } = "primary";
        public bool IncludeShellDefault { get; init; }
        public bool SameDiagnosticsReference { get; init; }
        public bool DifferentDiagnosticConnectionValues { get; init; }
        public bool AllLegacy { get; init; }
        public bool MixedDiagnostics { get; init; }
        public bool RuntimePair { get; init; }
        public bool ProviderMismatch { get; init; }
        public bool SchemaMismatch { get; init; }
        public string? InlineIdentityField { get; init; }
        public bool MissingBindingResource { get; init; }
    }
}
