using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivitiesRuntimeFeatureTests
{
    [Fact]
    public void RegistersActivityInvocationSchedulerWorkHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesRuntimeFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            descriptor.ImplementationType == typeof(WorkflowInvokeActivitySchedulerWorkHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer) &&
            descriptor.ImplementationType == typeof(RuntimeActivityInputMaterializer));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ActivityFactory>(provider.GetRequiredService<IActivityFactory>());
        Assert.IsType<RuntimeActivityInputMaterializer>(provider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.Contains(provider.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is WorkflowInvokeActivitySchedulerWorkHandler);
    }
}
