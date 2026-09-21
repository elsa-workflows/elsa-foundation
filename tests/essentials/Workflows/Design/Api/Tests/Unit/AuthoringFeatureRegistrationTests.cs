using Elsa.Workflows.Design.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class AuthoringFeatureRegistrationTests
{
    [Fact]
    public void Feature_registers_contextual_authoring_services()
    {
        var services = new ServiceCollection();

        new WorkflowsDesignApiFeature().ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == typeof(ActivityInputOptionsAuthoringService));
        Assert.Contains(services, x => x.ServiceType == typeof(IActivityInputOptionsProviderResolver));
    }
}
