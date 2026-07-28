using CShells.Features;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Groundwork.DiagnosticRecords;
using Groundwork.Sqlite.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;

/// <summary>
/// Selects the provider-level Elsa Groundwork families (runtime, secrets, distributed runtime,
/// workflows design, activities design and publishing) and exposes one SQLite physical store for
/// their validated host-selected composition. Runtime startup admits the exact applied target; schema
/// application remains an operator/CLI responsibility. Identity is selected explicitly by its own feature.
/// </summary>
public static class GroundworkSqliteUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string the single document store opens.</param>
    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false,
        bool skipInspectionWhenPlanUnchanged = false) =>
        services.AddGroundworkSqliteUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString, autoApplyOnStartup, skipInspectionWhenPlanUnchanged);

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false,
        bool skipInspectionWhenPlanUnchanged = false)
    {
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkSqliteUnifiedPersistenceCore(
            connectionString, autoApplyOnStartup, skipInspectionWhenPlanUnchanged);
    }

    /// <summary>
    /// Registers the unified SQLite substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false,
        bool skipInspectionWhenPlanUnchanged = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkSqliteUnifiedPersistenceCore(
            connectionString, autoApplyOnStartup, skipInspectionWhenPlanUnchanged);
    }

    private static IServiceCollection AddGroundworkSqliteUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup,
        bool skipInspectionWhenPlanUnchanged)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddSqliteGroundworkDocumentStore(
            connectionString, autoApplyOnStartup, skipInspectionWhenPlanUnchanged);
        services.TryAddSingleton<IDiagnosticRecordDeploymentApplier>(
            _ => SqliteDiagnosticRecordStoreFactory.CreateDeploymentApplier(connectionString));
        services.TryAddSingleton<IDiagnosticRecordStoreSessionFactory>(
            _ => SqliteDiagnosticRecordStoreFactory.CreateSessionFactory(connectionString));
        services.AddGroundworkDiagnosticRecordDeploymentInitializer(autoApplyOnStartup);
        services.AddGroundworkUnifiedStoreFamilies();
        services.AddGroundworkWorkflowRunHealth(
            _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
            GroundworkRunHealthDialect.Sqlite);
        services.AddGroundworkWorkflowPortfolio(
            _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
            GroundworkRunHealthDialect.Sqlite);
        return services;
    }
}
