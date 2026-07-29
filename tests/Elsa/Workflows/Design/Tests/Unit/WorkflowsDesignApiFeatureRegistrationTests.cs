using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
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
