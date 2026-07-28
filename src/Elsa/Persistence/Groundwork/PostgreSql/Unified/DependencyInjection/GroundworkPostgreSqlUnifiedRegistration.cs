using CShells.Features;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.DiagnosticRecords;
using Groundwork.PostgreSql.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;

/// <summary>
/// Selects the provider-level Elsa Groundwork families (runtime, secrets, distributed runtime,
/// workflows design, activities design and publishing) and exposes one PostgreSQL physical store for
/// their validated host-selected composition. Runtime startup admits the exact applied target; schema
/// application remains an operator/CLI responsibility. Identity is selected explicitly by its own feature.
/// </summary>
public static class GroundworkPostgreSqlUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string the single document store opens.</param>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString) =>
        services.AddGroundworkPostgreSqlUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            new WorkflowExecutableCacheOptions { Enabled = false });

    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkPostgreSqlUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            new WorkflowExecutableCacheOptions { Enabled = false },
            autoApplyOnStartup);

    /// <summary>Registers unified PostgreSQL persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        return services.AddGroundworkPostgreSqlUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkPostgreSqlUnifiedPersistence(
            connectionString,
            context,
            new WorkflowExecutableCacheOptions { Enabled = false },
            autoApplyOnStartup);

    /// <summary>Registers the current shell schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkPostgreSqlUnifiedPersistenceCore(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    /// <summary>
    /// Registers the unified PostgreSQL substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new() =>
        services.AddGroundworkPostgreSqlUnifiedPersistence<TDeploymentSource>(
            connectionString,
            new WorkflowExecutableCacheOptions { Enabled = false },
            autoApplyOnStartup);

    /// <summary>Registers an explicit deployment schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkPostgreSqlUnifiedPersistenceCore(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddPostgreSqlGroundworkDocumentStore(connectionString, autoApplyOnStartup);
        services.TryAddSingleton<IDiagnosticRecordStoreSessionFactory>(
            _ => PostgreSqlDiagnosticRecordStoreFactory.CreateSessionFactory(connectionString));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        services.AddGroundworkWorkflowRunHealth(
            _ => new Npgsql.NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql);
        services.AddGroundworkWorkflowPortfolio(
            _ => new Npgsql.NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql);
        return services;
    }
}
