using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The bridge does three things — read the Design seam, drive the Runtime seam, project both. These
/// tests pin that wiring with a fake factory (the factory's own dispatch is covered in
/// Elsa.Activities.Runtime.Tests).
/// </summary>
public sealed class ConstructActivityRequestHandlerTests
{
    [Fact]
    public async Task PassesTheRowsDescriptorTypeAndOpaquePayloadToTheFactory()
    {
        var payload = JsonSerializer.SerializeToElement(new { TypeName = "WriteLine" });
        var version = Version("v1", "def1", "1.0.0", descriptorType: "Elsa.Primitives.Models.TypeInformation", payload: payload);
        var factory = new FakeActivityFactory(new StubActivity());
        var handler = new ConstructActivityRequestHandler(new FakeActivityVersionStore([version]), factory);

        await handler.Handle(new ConstructActivity("v1"), CancellationToken.None);

        Assert.Equal("Elsa.Primitives.Models.TypeInformation", factory.LastDescriptorType);
        Assert.Equal("WriteLine", factory.LastPayload.GetProperty("TypeName").GetString());
        // Construct-only: no author values are bound yet.
        Assert.Null(factory.LastInputs);
        Assert.Null(factory.LastOutputs);
    }

    [Fact]
    public async Task ProjectsBothSeamsIntoTheView()
    {
        var version = Version("v1", "def1", "2.0.0",
            descriptorType: "Elsa.Workflows.Primitives.Models.WorkflowIdentity",
            payload: JsonSerializer.SerializeToElement(new { }),
            inputs: [new InputDefinition("text", "Text", new TypeReference("String"), null, "Text", null)],
            outputs: [new OutputDefinition("result", "Result", new TypeReference("Object"), null, "Result", null)]);
        var handler = new ConstructActivityRequestHandler(new FakeActivityVersionStore([version]), new FakeActivityFactory(new StubActivity()));

        var view = await handler.Handle(new ConstructActivity("v1"), CancellationToken.None);

        // Design side.
        Assert.Equal("Elsa.Workflows.Primitives.Models.WorkflowIdentity", view.DescriptorType);
        var input = Assert.Single(view.Inputs);
        Assert.Equal("text", input.ReferenceKey);
        Assert.Equal("String", input.TypeName);
        Assert.Equal("result", Assert.Single(view.Outputs).ReferenceKey);

        // Runtime side.
        Assert.Equal(typeof(StubActivity).FullName, view.RuntimeType);
        Assert.Equal("wf-123", Assert.Contains("WorkflowIdentity", view.SyntheticProperties));
        Assert.Equal("joey", Assert.Contains("author", view.CustomProperties));
        Assert.Equal("hello", Assert.Contains("Greeting", view.Properties)); // concrete-declared property
    }

    private static ActivityDefinitionVersion Version(
        string id,
        string definitionId,
        string version,
        string descriptorType,
        JsonElement payload,
        IEnumerable<InputDefinition>? inputs = null,
        IEnumerable<OutputDefinition>? outputs = null) =>
        new(version, definitionId)
        {
            Id = id,
            DescriptorType = descriptorType,
            DescriptorPayload = payload,
            Inputs = inputs ?? [],
            Outputs = outputs ?? []
        };
}
