using Elsa.Activities.DispatchWorkflow.Design;
using Elsa.Activities.DispatchWorkflow.Design.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowDesignFeatureTests
{
    [Fact]
    public void ConfigureServices_registers_options_and_compilation_sources_once()
    {
        var services = new ServiceCollection();
        var feature = new DispatchWorkflowDesignFeature();

        feature.ConfigureServices(services);
        feature.ConfigureServices(services);

        var optionsProvider = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IActivityInputOptionsProvider) &&
                          descriptor.ImplementationType == typeof(WorkflowDefinitionOptionsProvider));
        Assert.Equal(ServiceLifetime.Scoped, optionsProvider.Lifetime);

        var compilationSource = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IExecutableCompilationSource) &&
                          descriptor.ImplementationType == typeof(DispatchPinSource));
        Assert.Equal(ServiceLifetime.Scoped, compilationSource.Lifetime);

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IExecutableNodeMetadataSource) &&
                          descriptor.ImplementationType == typeof(DispatchPinSource));
    }
}
