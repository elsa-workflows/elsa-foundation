using CShells.Features;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.SqlServer;
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
        services.AddGroundworkSqlServerUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers unified SQL Server persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        return services.AddGroundworkSqlServerUnifiedPersistence<GroundworkAllFeaturesDeploymentSchema>(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    /// <summary>Registers the schema selected from the current shell's enabled feature descriptors.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        bool autoApplyOnStartup = false) =>
        services.AddGroundworkSqlServerUnifiedPersistence(
            connectionString,
            context,
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers the current shell schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        ShellFeatureContext context,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema(context);
        return services.AddGroundworkSqlServerUnifiedPersistenceCore(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    /// <summary>
    /// Registers the unified SQL Server substrate against an explicitly selected deployment schema.
    /// Feature services, including Identity, remain independently selected by the host.
    /// </summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new() =>
        services.AddGroundworkSqlServerUnifiedPersistence<TDeploymentSource>(
            connectionString,
            new WorkflowExecutableCacheOptions(),
            autoApplyOnStartup);

    /// <summary>Registers an explicit deployment schema with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence<TDeploymentSource>(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup = false)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkReferenceDeploymentSchema<TDeploymentSource>();
        return services.AddGroundworkSqlServerUnifiedPersistenceCore(
            connectionString,
            workflowExecutableCacheOptions,
            autoApplyOnStartup);
    }

    private static IServiceCollection AddGroundworkSqlServerUnifiedPersistenceCore(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        services.AddGroundworkStorageProviderConnection(
            _ => new SqlServerProviderFactory().Create(connectionString));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Both tiles read the v2 lanes through the storage session source, so they need no connection
        // of their own and follow whatever target each lane is bound to.
        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
        return services;
    }
}
