using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowAlterationRegistryTests
{
    [Fact]
    public void Registration_ReservesBuiltInNamesAndRequiresNamespacesForCustomKinds()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddWorkflowAlterationHandler<TestHandler>(new WorkflowAlterationDescriptor("CancelWorkflow", 1, "cancel")));
        Assert.Throws<ArgumentException>(() => services.AddWorkflowAlterationHandler<TestHandler>(new WorkflowAlterationDescriptor("Custom", 1, "custom")));
    }

    [Fact]
    public void Registry_UsesExactVersionRejectsDuplicatesAndKeepsHandlersScoped()
    {
        var services = new ServiceCollection();
        var descriptor = new WorkflowAlterationDescriptor("Contoso.Normalize", 2, "normalize");
        services.AddWorkflowAlterationHandler<TestHandler>(descriptor);

        Assert.Contains(services, item => item.ServiceType == typeof(TestHandler) && item.Lifetime == ServiceLifetime.Scoped);
        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowAlterationHandler<SecondHandler>(descriptor));

        services.AddWorkflowRuntime();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IWorkflowAlterationRegistry>();
        Assert.Equal(descriptor, registry.GetRequired("Contoso.Normalize", 2));
        Assert.Null(registry.Find("Contoso.Normalize", 1));
        Assert.DoesNotContain(registry.Descriptors, item => item.GetType().FullName!.Contains("Handler", StringComparison.Ordinal));
    }

    private sealed class TestHandler : IWorkflowAlterationPreflightHandler
    {
        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
    }

    private sealed class SecondHandler : IWorkflowAlterationPreflightHandler
    {
        public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowAlterationPreflightResult.Accepted());
    }
}
