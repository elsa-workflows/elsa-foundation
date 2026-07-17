using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ConstructActivityRequestHandlerTests
{
    [Fact]
    public async Task ProjectsAuthoredContractWithoutConstructingRuntimeActivity()
    {
        var payload = JsonSerializer.SerializeToElement(new { typeAlias = "WriteLine" });
        var version = new ActivityDefinitionVersion("2.0.0", "def1")
        {
            Id = "v1",
            DescriptorType = "Elsa.Primitives.Models.ClrActivityDescriptor",
            DescriptorPayload = payload,
            Inputs = [new InputDefinition("text", "Text", new TypeReference("String"), null, "Text", null)],
            Outputs = [new OutputDefinition("result", "Result", new TypeReference("Object"), null, "Result", null)]
        };
        var handler = new ConstructActivityRequestHandler(new FakeActivityVersionStore([version]));

        var view = await handler.Handle(new ConstructActivity("v1"), CancellationToken.None);

        Assert.Equal("v1", view.ActivityVersionId);
        Assert.Equal(version.DescriptorType, view.DescriptorType);
        Assert.Equal("WriteLine", view.DescriptorPayload.GetProperty("typeAlias").GetString());
        Assert.Equal("text", Assert.Single(view.Inputs).ReferenceKey);
        Assert.Equal("String", Assert.Single(view.Inputs).TypeName);
        Assert.Equal("result", Assert.Single(view.Outputs).ReferenceKey);
    }
}
