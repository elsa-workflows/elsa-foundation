using Elsa.Api.AspNetCore;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using NativeEndpoints;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowsDesignApiFeatureRegistrationTests
{
    [Fact]
    public void Feature_registers_scoped_variable_authoring_independently_of_validation_feature()
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ScopedVariableResolver>());
        Assert.NotNull(provider.GetRequiredService<ScopedVariablePicker>());
        Assert.NotNull(provider.GetRequiredService<ScopedVariableAuthoringContract>());
    }

    [Fact]
    public void Feature_registers_the_endpoint_readers_and_owner_keyed_failure_services()
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowDefinitionStore>(_ => null!);
        services.AddScoped<IWorkflowDefinitionVersionStore>(_ => null!);
        services.AddScoped<IWorkflowDefinitionDraftStore>(_ => null!);
        services.AddScoped<IWorkflowDefinitionVersionLayoutStore>(_ => null!);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<global::Elsa.Workflows.Design.Api.Endpoints.Definitions.IWorkflowDefinitionDetailsReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<global::Elsa.Workflows.Design.Api.Endpoints.Versions.IWorkflowVersionDetailsReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IEndpointProblemWriter>("Elsa.Workflows.Design.Api"));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IEndpointExceptionTranslator>("Elsa.Workflows.Design.Api"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Feature_registers_an_activatable_expression_authoring_context_source(bool registerDraftStore)
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        if (registerDraftStore)
            services.AddScoped<IWorkflowDefinitionDraftStore>(_ => null!);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IExpressionAuthoringContextSource>();

        Assert.IsType<PersistedExpressionAuthoringContextSource>(source);
    }
}
