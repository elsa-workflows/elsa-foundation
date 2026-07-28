extern alias DiagnosticsGroundwork;

using System.Reflection;
using CShells.Features;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Composition boundary for the optional durable diagnostics features. Provider choice remains a host concern:
/// these features select only Elsa's Groundwork adapters, never a SQLite, SQL Server, PostgreSQL, or MongoDB leaf.
/// </summary>
public sealed class DiagnosticsPersistenceFeatureTests
{
    [Fact]
    public void Disabled_persistence_features_leave_the_in_memory_diagnostics_stores_selected()
    {
        var services = ConfigureDiagnosticsDefaults();

        AssertSingleStore<IOpenTelemetryStore, InMemoryOpenTelemetryStore>(services);
        AssertSingleStore<IStructuredLogStore, InMemoryStructuredLogStore>(services);
    }

    [Fact]
    public void Enabled_Groundwork_persistence_features_replace_each_default_store_once()
    {
        var services = ConfigureDiagnosticsDefaults();

        ConfigureFeature<GroundworkOpenTelemetryStore>(services, "GroundworkOpenTelemetryPersistenceFeature");
        ConfigureFeature<GroundworkStructuredLogStore>(services, "GroundworkStructuredLogsPersistenceFeature");

        AssertSingleStore<IOpenTelemetryStore, GroundworkOpenTelemetryStore>(services);
        AssertSingleStore<IStructuredLogStore, GroundworkStructuredLogStore>(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryOpenTelemetryStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryStructuredLogStore));
    }

    [Fact]
    public void Enabled_combined_Groundwork_persistence_feature_replaces_both_default_stores_once()
    {
        var services = ConfigureDiagnosticsDefaults();

        new DiagnosticsGroundwork::Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkPersistenceFeature()
            .ConfigureServices(services);

        AssertSingleStore<IOpenTelemetryStore, GroundworkOpenTelemetryStore>(services);
        AssertSingleStore<IStructuredLogStore, GroundworkStructuredLogStore>(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryOpenTelemetryStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InMemoryStructuredLogStore));
    }

    [Fact]
    public void Each_enabled_Groundwork_feature_replaces_only_its_own_store_contract()
    {
        var telemetryOnly = ConfigureDiagnosticsDefaults();
        ConfigureFeature<GroundworkOpenTelemetryStore>(telemetryOnly, "GroundworkOpenTelemetryPersistenceFeature");

        AssertSingleStore<IOpenTelemetryStore, GroundworkOpenTelemetryStore>(telemetryOnly);
        AssertSingleStore<IStructuredLogStore, InMemoryStructuredLogStore>(telemetryOnly);

        var structuredLogsOnly = ConfigureDiagnosticsDefaults();
        ConfigureFeature<GroundworkStructuredLogStore>(structuredLogsOnly, "GroundworkStructuredLogsPersistenceFeature");

        AssertSingleStore<IOpenTelemetryStore, InMemoryOpenTelemetryStore>(structuredLogsOnly);
        AssertSingleStore<IStructuredLogStore, GroundworkStructuredLogStore>(structuredLogsOnly);
    }

    [Fact]
    public void Reapplying_the_OpenTelemetry_Groundwork_feature_is_idempotent()
    {
        var services = ConfigureDiagnosticsDefaults();
        var feature = new GroundworkOpenTelemetryPersistenceFeature();

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertSingleStore<IOpenTelemetryStore, GroundworkOpenTelemetryStore>(services);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Diagnostics.Persistence.Draining.IDiagnosticsPersistenceDrain));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Diagnostics.Persistence.Draining.IDiagnosticsPersistenceStartupResource));
    }

    [Fact]
    public void Reapplying_the_Structured_Logs_Groundwork_feature_is_idempotent()
    {
        var services = ConfigureDiagnosticsDefaults();
        var feature = new GroundworkStructuredLogsPersistenceFeature();

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        AssertSingleStore<IStructuredLogStore, GroundworkStructuredLogStore>(services);
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Diagnostics.Persistence.Draining.IDiagnosticsPersistenceDrain));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Diagnostics.Persistence.Draining.IDiagnosticsPersistenceStartupResource));
    }

    [Theory]
    [InlineData(typeof(GroundworkOpenTelemetryPersistenceFeature))]
    [InlineData(typeof(GroundworkStructuredLogsPersistenceFeature))]
    [InlineData(typeof(DiagnosticsGroundwork::Elsa.Diagnostics.Persistence.Groundwork.DiagnosticsGroundworkPersistenceFeature))]
    public void Groundwork_persistence_features_are_extensible_with_a_virtual_registration_entry_point(Type featureType)
    {
        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
        var configureServices = Assert.Single(featureType.GetMethods(), method =>
            method.Name == nameof(IShellFeature.ConfigureServices) &&
            method.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == typeof(IServiceCollection));
        Assert.True(configureServices.IsVirtual);
        Assert.False(configureServices.IsFinal);
    }

    [Fact]
    public void Shared_diagnostic_record_gate_is_a_public_sealed_composition_type()
    {
        var gateType = typeof(GroundworkDiagnosticRecordStoreGate);

        Assert.True(gateType.IsPublic);
        Assert.True(gateType.IsSealed);
        Assert.DoesNotContain(
            gateType.Assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>(),
            attribute => attribute.AssemblyName?.StartsWith("Elsa.Diagnostics.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Groundwork_diagnostics_features_leave_provider_leaf_selection_to_the_host()
    {
        var services = ConfigureDiagnosticsDefaults();

        ConfigureFeature<GroundworkOpenTelemetryStore>(services, "GroundworkOpenTelemetryPersistenceFeature");
        ConfigureFeature<GroundworkStructuredLogStore>(services, "GroundworkStructuredLogsPersistenceFeature");

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == "Elsa.Persistence.Groundwork.DependencyInjection.GroundworkProviderLeafSelection");
        AssertNoConcreteProviderReference(typeof(GroundworkOpenTelemetryStore).Assembly);
        AssertNoConcreteProviderReference(typeof(GroundworkStructuredLogStore).Assembly);
    }

    private static ServiceCollection ConfigureDiagnosticsDefaults()
    {
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        new StructuredLogsFeature().ConfigureServices(services);
        return services;
    }

    private static void ConfigureFeature<TAssemblyAnchor>(IServiceCollection services, string featureTypeName)
    {
        var featureType = typeof(TAssemblyAnchor).Assembly.GetType(
            $"{typeof(TAssemblyAnchor).Namespace}.{featureTypeName}",
            throwOnError: false);
        Assert.NotNull(featureType);

        var feature = Assert.IsAssignableFrom<IShellFeature>(Activator.CreateInstance(featureType!));
        feature.ConfigureServices(services);
    }

    private static void AssertSingleStore<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var contract = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TContract));
        var implementation = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TImplementation));

        Assert.Equal(ServiceLifetime.Singleton, contract.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, implementation.Lifetime);
        Assert.NotNull(contract.ImplementationFactory);
    }

    private static void AssertNoConcreteProviderReference(System.Reflection.Assembly assembly)
    {
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is not null &&
            (reference.Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ||
             reference.Name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
             reference.Name.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase) ||
             reference.Name.Contains("Mongo", StringComparison.OrdinalIgnoreCase)));
    }
}
