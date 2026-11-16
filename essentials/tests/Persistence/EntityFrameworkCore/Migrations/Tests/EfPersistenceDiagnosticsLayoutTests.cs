using CShells;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class EfPersistenceDiagnosticsLayoutTests
{
    [Theory]
    [InlineData("Data Source=diagnostics.db", false)]
    [InlineData("Data Source=other.db", true)]
    public void Both_diagnostics_stores_must_have_one_effective_target_even_under_distinct_bindings(
        string telemetryConnection, bool incompatible)
    {
        var context = Context(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:DiagnosticsStructuredLogsEntityFrameworkCore"] = "logs",
            ["Elsa:Persistence:Bindings:DiagnosticsOpenTelemetryEntityFrameworkCore"] = "telemetry"
        });
        var configuration = Configuration(telemetryConnection);

        var live = EfPersistencePreparation.Prepare(context, configuration);
        var offline = EfPersistencePreparation.Prepare(context, configuration, verifyConnectionValues: false);

        if (incompatible)
        {
            Assert.Contains("resource-context-conflict", live.RefusalCodes);
            Assert.Empty(live.Patch.ConfigurationData);
        }
        else
        {
            Assert.Empty(live.RefusalCodes);
            Assert.Equal(6, live.Patch.ConfigurationData.Count);
        }
        Assert.Empty(offline.RefusalCodes);
        Assert.Contains("target-affinity-unverified", offline.UnresolvedCodes);
        Assert.DoesNotContain("Data Source=", System.Text.Json.JsonSerializer.Serialize(live));
    }

    [Fact]
    public void One_resource_selected_diagnostic_and_one_legacy_diagnostic_is_not_a_supported_split()
    {
        var context = Context(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:DiagnosticsStructuredLogsEntityFrameworkCore"] = "logs"
        });

        var result = EfPersistencePreparation.Prepare(context,
            Configuration("Data Source=diagnostics.db", includeDefault: false));

        Assert.Contains("resource-ownership-unresolved", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Fact]
    public void Diagnostics_stores_on_distinct_providers_refuse_even_when_both_are_named_resources()
    {
        var context = Context(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:DiagnosticsStructuredLogsEntityFrameworkCore"] = "logs",
            ["Elsa:Persistence:Bindings:DiagnosticsOpenTelemetryEntityFrameworkCore"] = "telemetry"
        });

        var result = EfPersistencePreparation.Prepare(context,
            Configuration("Host=localhost;Database=diagnostics", telemetryProvider: "PostgreSql"));

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    private static IConfigurationRoot Configuration(
        string telemetryConnection, bool includeDefault = true, string telemetryProvider = "Sqlite")
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Primary",
            ["Elsa:Persistence:Resources:logs:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:logs:ConnectionName"] = "Logs",
            ["Elsa:Persistence:Resources:telemetry:Provider"] = telemetryProvider,
            ["Elsa:Persistence:Resources:telemetry:ConnectionName"] = "Telemetry",
            ["ConnectionStrings:Primary"] = "Data Source=primary.db",
            ["ConnectionStrings:Logs"] = "Data Source=diagnostics.db",
            ["ConnectionStrings:Telemetry"] = telemetryConnection
        };
        if (includeDefault)
            values["Elsa:Persistence:DefaultResource"] = "primary";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ShellSettingsPreparationContext Context(IReadOnlyDictionary<string, string?> values)
    {
        ShellFeaturePreparationDescriptor[] features =
        [
            new("WorkflowsRuntimeEntityFrameworkCore", [], typeof(RuntimeEntityFrameworkCoreFeature), false),
            new("DiagnosticsStructuredLogsEntityFrameworkCore", [], typeof(StructuredLogsEntityFrameworkCoreFeature), false),
            new("DiagnosticsOpenTelemetryEntityFrameworkCore", [], typeof(EfOpenTelemetryFeature), false)
        ];
        var ids = features.Select(feature => feature.Id).ToArray();
        return new ShellSettingsPreparationContext(new ShellId("default"), values, ids, [], [], features, ids, [], []);
    }
}
