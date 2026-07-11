using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Services;
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
}
