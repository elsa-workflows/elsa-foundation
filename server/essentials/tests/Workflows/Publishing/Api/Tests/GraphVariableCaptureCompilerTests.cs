using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>A body node's capture into a graph variable is pinned to the variable's declared type and driver.</summary>
public sealed class GraphVariableCaptureCompilerTests
{
    [Theory]
    [InlineData("System.Text.Json.JsonElement?")]
    [InlineData("Object")]
    [InlineData("String")]
    public void Any_activity_output_can_be_captured_into_a_json_variable(string sourceAlias)
    {
        var compiler = new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        var captures = compiler.CompileGraphVariableOutputs("custom-activity",
            [new ActivityResultProjectionContract("data", "$", new ValueTypeDescriptor(sourceAlias), isRequired: false, ActivityValuePolicy.Default)],
            [new ArgumentState("data", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "json", declaringScopeId = "workflow" }), "Variable"), null, null, null, null, new AuthoredValueConversionRequest())],
            [new GraphVariableCaptureTarget("json", "Data", JsonSerializer.SerializeToElement(new { alias = "System.Text.Json.JsonElement", collectionKind = "Single" }), WellKnownRuntimeDurableValueStorageDrivers.Json)], "custom-container");

        var capture = Assert.Single(captures.Values);
        Assert.Equal("System.Text.Json.JsonElement", capture.ConversionPlan!.TargetType.Alias);
        Assert.Equal(ValueConversionOperation.CanonicalAny, capture.ConversionPlan.Operation);
    }

    private readonly RuntimeOutputCaptureCompiler _compiler =
        new(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));

    [Fact]
    public void A_conversion_into_a_collection_variable_keeps_the_collection_kind()
    {
        var collection = new ValueTypeDescriptor("Object", CollectionKind.Array);
        var captures = _compiler.CompileGraphVariableOutputs(
            "lookup",
            [new ActivityResultProjectionContract("items", "$", collection, isRequired: false, ActivityValuePolicy.Default)],
            [new ArgumentState(
                "items",
                new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "list", declaringScopeId = "workflow" }), "Variable"),
                null, null, null, null,
                new AuthoredValueConversionRequest())],
            [new GraphVariableCaptureTarget("list", "List", JsonSerializer.SerializeToElement(new { alias = "Object", collectionKind = "Array" }), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            "node-boundary");

        var capture = Assert.Single(captures.Values);
        Assert.Equal(CollectionKind.Array, capture.ConversionPlan!.TargetType.CollectionKind);
        Assert.Equal("node-boundary", capture.Metadata[RuntimeMetadataKeys.CaptureTargetScopeId]);
    }
}
