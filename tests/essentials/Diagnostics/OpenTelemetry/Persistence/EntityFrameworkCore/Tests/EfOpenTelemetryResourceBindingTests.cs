using System.Text;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryResourceBindingTests
{
    private const string FeatureId = "DiagnosticsOpenTelemetryEntityFrameworkCore";

    [Fact]
    public void Explicit_binding_materializes_resource_target_and_removal_restores_default_inheritance()
    {
        var bound = Prepare("diagnostics");
        var inherited = Prepare(binding: null);

        Assert.True(bound.HasApplicableResource);
        Assert.Empty(bound.RefusalCodes);
        Assert.Equal("Sqlite", bound.Patch.ConfigurationData[$"{FeatureId}:Provider"]);
        Assert.Equal("OtelStore", bound.Patch.ConfigurationData[$"{FeatureId}:ConnectionName"]);
        Assert.Contains(bound.Participants, item => item.FeatureId == FeatureId && item.ResourceName == "diagnostics");

        Assert.True(inherited.HasApplicableResource);
        Assert.Empty(inherited.RefusalCodes);
        Assert.Equal("Sqlite", inherited.Patch.ConfigurationData[$"{FeatureId}:Provider"]);
        Assert.Equal("Shared", inherited.Patch.ConfigurationData[$"{FeatureId}:ConnectionName"]);
        Assert.Contains(inherited.Participants, item => item.FeatureId == FeatureId && item.ResourceName == "primary");
    }

    [Fact]
    public void Resource_mode_does_not_enroll_custom_private_stores_or_change_legacy_telemetry_default()
    {
        var result = Prepare(binding: null, extraBinding: "PrivateTelemetryStore");
        var legacy = PrepareWithoutResources();

        Assert.Contains("resource-participant-unenrolled", result.UnresolvedCodes);
        Assert.DoesNotContain(result.Participants, item => item.FeatureId == "PrivateTelemetryStore");
        Assert.Empty(legacy.Patch.ConfigurationData);
        Assert.False(legacy.HasApplicableResource);
        Assert.Equal("ElsaOpenTelemetry", EfOpenTelemetryModule.DefaultConnectionName);
        Assert.Null(new EfOpenTelemetryFeature().ConnectionName);
    }

    [Fact]
    public void Missing_binding_resource_refuses_without_exposing_authored_value()
    {
        var result = Prepare("missing;Password=secret-canary");

        Assert.Contains("resource-selection-invalid", result.RefusalCodes);
        Assert.DoesNotContain("secret-canary", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static EfPersistencePreparationResult Prepare(string? binding, string? extraBinding = null)
    {
        var json = $$"""
            {
              "Elsa": {
                "Persistence": {
                  "Resources": {
                    "primary": { "Provider": "Sqlite", "ConnectionName": "Shared" },
                    "diagnostics": { "Provider": "Sqlite", "ConnectionName": "OtelStore" }
                  },
                  "DefaultResource": "primary"
                }
              },
              "CShells": { "Shells": { "default": { "Configuration": { "Elsa": { "Persistence": {
                "DefaultResource": "primary",
                "Bindings": { {{BindingEntries(binding, extraBinding)}} }
              } } } } } }
            }
            """;
        var configuration = CreateConfiguration(json);
        return EfPersistencePreparation.Prepare(Context(), configuration,
            [typeof(EfOpenTelemetryFeature).Assembly], verifyConnectionValues: false);
    }

    private static EfPersistencePreparationResult PrepareWithoutResources()
    {
        var configuration = CreateConfiguration("{}");
        return EfPersistencePreparation.Prepare(Context(), configuration,
            [typeof(EfOpenTelemetryFeature).Assembly], verifyConnectionValues: false);
    }

    private static string BindingEntries(string? binding, string? extraBinding) => string.Join(", ", new[]
    {
        binding is null ? null : $"\"{FeatureId}\": \"{binding}\"",
        extraBinding is null ? null : $"\"{extraBinding}\": \"primary\""
    }.OfType<string>());

    private static IConfiguration CreateConfiguration(string json) => new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
        .Build();

    private static ShellSettingsPreparationContext Context()
    {
        var feature = new ShellFeaturePreparationDescriptor(FeatureId, [], typeof(EfOpenTelemetryFeature), false);
        return new ShellSettingsPreparationContext(
            new ShellId("default"),
            new Dictionary<string, string?>(),
            [FeatureId],
            [],
            [],
            [feature],
            [FeatureId],
            [],
            []);
    }
}
