using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Design;
using Elsa.Workflows.Publishing.Core.Contracts;
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
    public void Runtime_registration_excludes_authoring_and_compilation_contributions()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = Assert.Single(services, service => service.ServiceType == typeof(IBpmnProcessRuntime));
        Assert.Equal(typeof(BpmnProcessRuntime), runtime.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, runtime.Lifetime);
        Assert.Single(services, service => service.ServiceType == typeof(BpmnStateCodec));
        Assert.Single(services, service => service.ServiceType == typeof(BpmnWorkLedger));

        Assert.Empty(provider.GetServices<IActivityStructureHandler>());
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IExecutableCompilationSource));

    }
    [Fact]
    public void Design_registration_provides_authoring_without_runtime_services_and_is_repeatable()
    {
        var services = new ServiceCollection();
        var feature = new ActivitiesBpmnDesignFeature();
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(Activities.BpmnProcess.StructureKind, handler.Kind);
        Assert.True(handler.SupportsScopedVariables);
        Assert.Equal(typeof(Models.BpmnAuthoredStructure), handler.AuthoredPayloadType);
        Assert.Single(services, service => service.ServiceType == typeof(IExecutableCompilationSource));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IBpmnProcessRuntime));
        Assert.DoesNotContain(typeof(ActivitiesBpmnDesignFeature).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "Elsa.Activities.Bpmn" or "Bpmn.Model" or "Bpmn.Semantics");
    }

    [Fact]
    public void Existing_runtime_assembly_forwards_neutral_models_without_forwarding_authored_types()
    {
        var assembly = typeof(Activities.BpmnProcess).Assembly;
        var forwarded = assembly.GetForwardedTypes();
        Assert.Contains(typeof(Models.BpmnStructure), forwarded);
        Assert.Contains(typeof(Models.BpmnElement), forwarded);
        Assert.Contains(typeof(Models.BpmnEventDefinition), forwarded);
        Assert.DoesNotContain(typeof(Models.BpmnAuthoredStructure), forwarded);
        Assert.All(forwarded, type => Assert.Equal("Elsa.Activities.Bpmn.Core", type.Assembly.GetName().Name));
        Assert.Equal("Elsa.Activities.Bpmn.Design", typeof(Models.BpmnAuthoredStructure).Assembly.GetName().Name);
    }

}
