using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnInterchangeRegistrationTests
{
    private const string TimerDocument = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="defs" targetNamespace="urn:test">
          <process id="p" isExecutable="true"><startEvent id="start"/>
            <intermediateCatchEvent id="wait"><timerEventDefinition><timeDuration>PT5M</timeDuration></timerEventDefinition></intermediateCatchEvent>
            <endEvent id="end"/><sequenceFlow id="first" sourceRef="start" targetRef="wait"/><sequenceFlow id="last" sourceRef="wait" targetRef="end"/>
          </process>
        </definitions>
        """;

    [Fact]
    public async Task Registered_import_export_and_work_binder_form_the_actual_conversion_path()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        await Assert.Single(provider.GetServices<IShellInitializer>().OfType<BpmnInterchangeRegistrationInitializer>()).InitializeAsync(CancellationToken.None);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBpmnWorkBinder>());
        var imported = scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>().Import(TimerDocument);
        Assert.False(imported.Analysis.CanExecute); // Conversion alone has no host inventory.
        Assert.Contains(imported.Analysis.Capabilities, capability => capability.Availability == BpmnImportCapabilityAvailability.Unknown);
        var exported = scope.ServiceProvider.GetRequiredService<IBpmnDocumentExporter>().Export(imported.ProcessNode);
        Assert.Contains("PT5M", exported, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("importer")]
    [InlineData("exporter")]
    [InlineData("binder")]
    public async Task Startup_rejects_a_missing_required_interchange_registration(string missing)
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        var contract = missing switch
        {
            "importer" => typeof(IBpmnDocumentImporter), "exporter" => typeof(IBpmnDocumentExporter), _ => typeof(IBpmnWorkBinder)
        };
        services.RemoveAll(contract);
        using var provider = services.BuildServiceProvider();
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>().OfType<BpmnInterchangeRegistrationInitializer>());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync(CancellationToken.None));
        Assert.Contains(contract.Name, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_rejects_competing_binders_instead_of_selecting_the_last_one()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        services.AddScoped<IBpmnWorkBinder, BpmnWorkBinder>();
        using var provider = services.BuildServiceProvider();
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>().OfType<BpmnInterchangeRegistrationInitializer>());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync(CancellationToken.None));
        Assert.Contains("exactly one IBpmnWorkBinder", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_host_binding_policy_is_used_by_the_registered_importer()
    {
        var services = new ServiceCollection();
        var policy = new HostBindingPolicy();
        services.AddScoped<IBpmnWorkBinder>(_ => policy);
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var imported = scope.ServiceProvider.GetRequiredService<IBpmnDocumentImporter>().Import(TimerDocument);
        Assert.Equal(1, policy.Calls);
        Assert.False(imported.Analysis.CanExecute);
        Assert.Contains("host.duration-wait", imported.ProcessNode.Structure!.Payload.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class HostBindingPolicy : IBpmnWorkBinder
    {
        public int Calls { get; private set; }
        public BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request)
        {
            Calls++;
            Assert.Equal(BpmnWorkBindingKind.TimerWait, request.Kind);
            Assert.Equal("PT5M", request.Value);
            return new(new ActivityNode(request.BindingRef, "host.duration-wait", [], []), BpmnImportCapabilityAvailability.Available, "Host binding");
        }
    }
}
