using Elsa.Tasks.Core;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa3.Activities.Design.Import.Reconciliation;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec 235: the feature validates its options at registration and wires its task and the startup grant.</summary>
public sealed class Elsa3JsonReconciliationFeatureTests
{
    [Fact]
    public void A_missing_source_id_is_refused_at_registration()
    {
        var feature = new Elsa3JsonReconciliationFeature { Options = new() { FolderPath = "elsa3" } };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(Services()));
        Assert.Contains("SourceId", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Not_exactly_one_path_option_is_refused_at_registration(bool filePath, bool files, bool folderPath)
    {
        var options = new Elsa3JsonReconciliationOptions
        {
            SourceId = "elsa3",
            FilePath = filePath ? "one.json" : null,
            Files = files ? [new Elsa3JsonReconciliationFileOption(0, "two.json")] : [],
            FolderPath = folderPath ? "elsa3" : null
        };

        var exception = Assert.Throws<InvalidOperationException>(() => new Elsa3JsonReconciliationFeature { Options = options }.ConfigureServices(Services()));
        Assert.Contains("exactly one of", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_feature_registers_its_task_and_wraps_the_authorization_context()
    {
        var services = Services();

        new Elsa3JsonReconciliationFeature { Options = new() { SourceId = "elsa3", FolderPath = "elsa3" } }.ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IStartupTask) && descriptor.ImplementationType == typeof(Elsa3FolderImportTask));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ImportedWorkflowPublisher));
        var context = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityPublishingAuthorizationContext));
        Assert.Equal(ServiceLifetime.Scoped, context.Lifetime);
        Assert.NotNull(context.ImplementationFactory);
    }

    [Fact]
    public void Without_a_registered_authorization_context_the_feature_names_what_it_needs()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new Elsa3JsonReconciliationFeature { Options = new() { SourceId = "elsa3", FolderPath = "elsa3" } }.ConfigureServices(new ServiceCollection()));
        Assert.Contains("WorkflowsPublishingApi", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>What <c>WorkflowsPublishingApi</c>, a dependency, registers before this feature runs.</summary>
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityPublishingAuthorizationContext, NoTenantContext>();
        return services;
    }

    private sealed class NoTenantContext : IActivityPublishingAuthorizationContext
    {
        public string? TenantId => null;

        public bool CanAccessTenant(string? tenantId) => tenantId is null;
    }
}
