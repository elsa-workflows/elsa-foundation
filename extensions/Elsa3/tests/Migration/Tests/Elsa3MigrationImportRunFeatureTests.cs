using System.Reflection;
using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Migration.Tooling;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T013: the tooling's shell feature registers the import run, its decoration and both writers.</summary>
public sealed class Elsa3MigrationImportRunFeatureTests
{
    [Fact]
    public void Registers_the_import_run_the_authorization_decoration_and_both_writers()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityPublishingAuthorizationContext, NoTenantContext>();

        new Elsa3MigrationImportRunFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(Elsa3ImportRun));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ImportedWorkflowPublisher));
        var context = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityPublishingAuthorizationContext));
        Assert.NotNull(context.ImplementationFactory);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IJsonWorkflowCatalogWriter));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IJsonActivityCatalogWriter));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IStartupTask) && descriptor.ImplementationType == typeof(Elsa3FolderImportTask));
    }

    [Fact]
    public void Is_named_and_depends_on_the_publishers_and_tasks()
    {
        var attribute = typeof(Elsa3MigrationImportRunFeature).GetCustomAttribute<ShellFeatureAttribute>()!;

        Assert.Equal("Elsa3MigrationImportRun", attribute.Name);
        Assert.Equal(["Elsa3ImportActivitiesPublishing", "Tasks", "WorkflowsPublishing", "WorkflowsPublishingApi"],
            attribute.DependsOn.Select(dependency => dependency.ToString()!).Order(StringComparer.Ordinal));
    }

    private sealed class NoTenantContext : IActivityPublishingAuthorizationContext
    {
        public string? TenantId => null;

        public bool CanAccessTenant(string? tenantId) => tenantId is null;
    }
}
