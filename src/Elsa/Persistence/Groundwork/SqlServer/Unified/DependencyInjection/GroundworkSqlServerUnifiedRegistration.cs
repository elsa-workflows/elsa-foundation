using CShells.Features;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.DiagnosticRecords;
using Groundwork.SqlServer.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;

/// <summary>Registers the provider-level store families against one SQL Server Groundwork target; Identity is selected explicitly by its own feature.</summary>
public static class GroundworkSqlServerUnifiedRegistration
{
    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkSqlServerUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(connectionString, autoApplyOnStartup);

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false)
    {
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkSqlServerUnifiedPersistenceCore(connectionString, autoApplyOnStartup);
    }

    /// <summary>
    /// Registers the unified SQL Server substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkSqlServerUnifiedPersistenceCore(connectionString, autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkSqlServerUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSqlServerGroundworkDocumentStore(connectionString, autoApplyOnStartup);
        services.TryAddSingleton<IDiagnosticRecordDeploymentApplier>(
            _ => SqlServerDiagnosticRecordStoreFactory.CreateDeploymentApplier(connectionString));
        services.TryAddSingleton<IDiagnosticRecordStoreSessionFactory>(
            _ => SqlServerDiagnosticRecordStoreFactory.CreateSessionFactory(connectionString));
        services.AddGroundworkDiagnosticRecordDeploymentInitializer(autoApplyOnStartup);
        return services.AddGroundworkUnifiedStoreFamilies();
    }
}
