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

    [Fact]
    public async Task Runtime_startup_refuses_a_second_invocation_inspector_instead_of_letting_the_last_one_win()
    {
        var services = new ServiceCollection();
        new SpecialistsRuntimeFeature().ConfigureServices(services);
        new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        var check = (SpecialistRegistrationStartupTask)Assert.Single(services, x => x.ImplementationInstance is SpecialistRegistrationStartupTask).ImplementationInstance!;
        await check.ExecuteAsync(CancellationToken.None);

        services.AddScoped<ISpecialistInvocationInspector, HostInspector>();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => check.ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(ISpecialistInvocationInspector), refusal.Message, StringComparison.Ordinal);
    }

    private sealed class HostInspector : ISpecialistInvocationInspector
    {
        public ValueTask<SpecialistInvocationEvidence?> InspectAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SpecialistInvocationEvidence?>(null);
    }
}
