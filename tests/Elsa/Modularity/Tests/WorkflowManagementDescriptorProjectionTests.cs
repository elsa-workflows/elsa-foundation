using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText.Services;
using Elsa.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class WorkflowManagementDescriptorProjectionTests
{
    [Fact]
    public void InputDescriptorProjection_IncludesOrderAndDefaultMetadata()
    {
        var input = new InputDefinition(
            ReferenceKey: "Step",
            Name: "Step",
            Type: new TypeReference("Int32"),
            StorageDriverType: null,
            DisplayName: "Step",
            Category: null,
            Order: 20,
            DefaultValue: JsonSerializer.SerializeToElement(1),
            DefaultSyntax: "Literal");
        var response = InvokeToInputDescriptorResponse(input);

        Assert.Equal(20f, Read<float>(response, "Order"));
        Assert.Equal("Literal", Read<string>(response, "DefaultSyntax"));

        var defaultValue = Assert.IsType<JsonElement>(Read<object>(response, "DefaultValue"));
        Assert.Equal(JsonValueKind.Number, defaultValue.ValueKind);
        Assert.Equal(1, defaultValue.GetInt32());
    }

    [Fact]
    public void InputDescriptorProjection_PreservesAuthoredUiHintAndSpecifications()
    {
        var specifications = JsonSerializer.SerializeToElement(new
        {
            options = new[]
            {
                new { label = "GET", value = "GET" },
                new { label = "POST", value = "POST" }
            }
        });
        var input = new InputDefinition(
            ReferenceKey: "Methods",
            Name: "Methods",
            Type: new TypeReference("String", CollectionKind.List),
            StorageDriverType: null,
            DisplayName: "Methods",
            Category: null,
            UiHint: "checklist",
            UISpecifications: specifications);

        var response = InvokeToInputDescriptorResponse(input);

        Assert.Equal("checklist", Read<string>(response, "UiHint"));
        var projected = Assert.IsType<JsonElement>(Read<object>(response, "UiSpecifications"));
        Assert.Equal(specifications.GetRawText(), projected.GetRawText());
    }

    private static object InvokeToInputDescriptorResponse(InputDefinition input)
    {
        var method = typeof(ElsaWorkflowManagementApi).GetMethod("ToInputDescriptorResponse", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ElsaWorkflowManagementApi), "ToInputDescriptorResponse");

        return method.Invoke(null, [input, NullLogger.Instance, new WellKnownTypeRegistry()])!;
    }

    private static T Read<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName)
            ?? throw new MissingMemberException(source.GetType().FullName, propertyName);
        return Assert.IsAssignableFrom<T>(property.GetValue(source));
    }
}
