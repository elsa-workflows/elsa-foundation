using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeFeatureTests
{
    [Fact]
    public void AddWorkflowRuntime_ComposesAlterationRegistryAndProcessLocalDevelopmentProtector()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.Contains(firstScope.ServiceProvider.GetRequiredService<IWorkflowAlterationRegistry>().Descriptors, descriptor => descriptor.Kind == "CancelWorkflow" && descriptor.SchemaVersion == 1);
        var first = firstScope.ServiceProvider.GetRequiredService<IWorkflowAlterationPayloadProtector>();
        var payload = first.Protect("plan-1", "tenant-a", "request-hash", "{}");
        var second = secondScope.ServiceProvider.GetRequiredService<IWorkflowAlterationPayloadProtector>();

        Assert.Equal("{}", second.Unprotect("plan-1", "tenant-a", "request-hash", payload));
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services.Where(item => item.ServiceType == typeof(IWorkflowAlterationPayloadProtector))).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services.Where(item => item.ServiceType == typeof(IWorkflowAlterationRegistry))).Lifetime);
        Assert.Equal("in-memory-development", payload.KeyId);
    }
}
