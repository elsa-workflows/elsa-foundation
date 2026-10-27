using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class ActivitiesBpmnFeatureTests
{
    [Fact]
    public async Task Duplicate_runtime_replacements_are_rejected_at_shell_startup()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnFeature().ConfigureServices(services);
        services.AddScoped<IBpmnProcessRuntime, BpmnProcessRuntime>();
        using var provider = services.BuildServiceProvider();

        var initializer = Assert.Single(provider.GetServices<CShells.Lifecycle.IShellInitializer>());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync(CancellationToken.None));
        Assert.Contains("exactly one IBpmnProcessRuntime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_RegistersReleasedRuntimeAndStructureHandler()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = Assert.Single(services, service => service.ServiceType == typeof(IBpmnProcessRuntime));
        Assert.Equal(typeof(BpmnProcessRuntime), runtime.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, runtime.Lifetime);
        Assert.Single(services, service => service.ServiceType == typeof(BpmnStateCodec));
        Assert.Single(services, service => service.ServiceType == typeof(BpmnWorkLedger));

        var structureHandler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(Activities.BpmnProcess.StructureKind, structureHandler.Kind);
        Assert.True(structureHandler.SupportsScopedVariables);
        Assert.Equal(typeof(Models.BpmnAuthoredStructure), structureHandler.AuthoredPayloadType);

    }
}
