using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Endpoints;
using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CapabilityEndpoint = Elsa.Activities.Bpmn.Interchange.Endpoints.Documents.Capabilities.Endpoint;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnAuthoringCapabilitiesTests
{
    [Fact]
    public async Task Composed_capability_names_the_handler_that_actually_retains_advanced_source_without_granting_execution()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IActivityStructureHandler>().ToArray();
        var capability = await new CapabilityEndpoint(handlers).HandleAsync(default);
        var handler = Assert.Single(handlers, handler => handler.Kind == capability.StructureKind && handler.SchemaVersion == capability.SchemaVersion);
        const string xml = """
            <b:definitions xmlns:b="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:v="urn:retained" targetNamespace="urn:test">
              <b:process id="P"><b:complexGateway id="Advanced"/><b:extensionElements><v:keep>exact</v:keep></b:extensionElements></b:process>
            </b:definitions>
            """;
        var node = new ActivityNode("owner", "actual-version", [], [], new(capability.StructureKind, capability.SchemaVersion,
            JsonSerializer.SerializeToElement(new { documentXml = xml, processId = "P", activities = Array.Empty<ActivityNode>(), bindings = Array.Empty<object>(), variables = Array.Empty<object>() })));
        Assert.Empty(Assert.Single(handler.ProjectChildren(node)).Activities);
        Assert.Equal(xml, new BpmnDocumentExporter().Export(node));
        var refusal = Assert.Throws<NotSupportedException>(() => handler.CompileExecutableStructure(node));
        Assert.Contains("complexGateway", refusal.Message, StringComparison.Ordinal);
        var serialized = JsonSerializer.Serialize(capability, BpmnInterchangeJsonContext.Default.BpmnDocumentAuthoringCapabilities);
        Assert.Equal("{\"structureKind\":\"elsa.bpmn.structure\",\"schemaVersion\":\"2.0.0\"}", serialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_competing_source_handlers_cannot_advertise_editing_compatibility(bool competing)
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnInterchangeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IActivityStructureHandler>().ToArray();
        var source = Assert.Single(handlers, handler => handler.Kind == "elsa.bpmn.structure" && handler.SchemaVersion == "2.0.0");
        var mismatched = competing ? handlers.Append(source) : handlers.Where(handler => !ReferenceEquals(handler, source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CapabilityEndpoint(mismatched).HandleAsync(default));
    }
}
