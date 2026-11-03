using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed class SpecialistsRuntimeFeatureRegistrationTests
{
    [Fact]
    public void Runtime_registers_the_scoped_invocation_inspector_as_a_replaceable_default()
    {
        var services = new ServiceCollection();
        new SpecialistsRuntimeFeature().ConfigureServices(services);
        var inspector = Assert.Single(services, x => x.ServiceType == typeof(ISpecialistInvocationInspector));
        Assert.Equal(ServiceLifetime.Scoped, inspector.Lifetime);
        Assert.Equal(typeof(SpecialistInvocationInspector), inspector.ImplementationType);

        var replaced = new ServiceCollection();
        replaced.AddScoped<ISpecialistInvocationInspector, HostInspector>();
        new SpecialistsRuntimeFeature().ConfigureServices(replaced);
        Assert.Equal(typeof(HostInspector), Assert.Single(replaced, x => x.ServiceType == typeof(ISpecialistInvocationInspector)).ImplementationType);
    }

    private sealed class HostInspector : ISpecialistInvocationInspector
    {
        public ValueTask<SpecialistInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SpecialistInvocationEvidence?>(null);
    }
}
