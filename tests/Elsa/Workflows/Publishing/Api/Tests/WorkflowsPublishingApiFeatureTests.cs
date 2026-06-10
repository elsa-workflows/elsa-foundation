using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowsPublishingApiFeatureTests
{
    [Fact]
    public void RegistersPublishingRequestHandlers()
    {
        var services = new ServiceCollection();

        new WorkflowsPublishingApiFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkflowExecutableStore) &&
            descriptor.ImplementationType == typeof(InMemoryWorkflowExecutableStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));
    }
}
