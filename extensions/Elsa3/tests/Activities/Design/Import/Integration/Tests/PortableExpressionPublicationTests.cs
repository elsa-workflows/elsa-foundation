using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class PortableExpressionPublicationTests
{
    private const string RawDefault = "{ \"OwnerKey\":\"Ren\\u00e9\", \"N\":1.0, \"x\":null }";

    [Theory]
    [InlineData("headers", "headers")]
    [InlineData("queryStringData", "query")]
    public async Task Captured_HTTP_request_projections_publish_with_exact_result_aliases(string capture, string suffix)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("http-response-content-type"))!.AsArray();
        var source = collection[0]!;
        source["variables"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "CapturedRequest", ["name"] = "CapturedRequest", ["typeName"] = "Object"
        });
        source["root"]!["activities"]![0]![capture] = new JsonObject
        {
            ["typeName"] = "Object", ["memoryReference"] = new JsonObject { ["id"] = "CapturedRequest" }
        };
        var original = source.ToJsonString();
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var selected = Assert.Single(items);
        const string sourceVersionId = "http-response-content-type-v1";
        await host.SelectAsync(handle, plan, sourceVersionId);
        var applied = await host.ApplyAsync(handle, plan, "import-captured-request", sourceVersionId);
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var versionId = Elsa3ImportHost.Required(selected, "workflowVersionId");
        var projectionId = "endpoint.http-" + suffix;

        await host.ReadAsync(async services =>
        {
            var version = await services.GetRequiredService<IWorkflowDefinitionVersionStore>().GetAsync(versionId);
            var node = Elsa3ImportedActivityNodes.Walk(version.State!.RootActivity!).Single(node => node.NodeId == projectionId);
            var argument = Assert.Single(node.Inputs, input => input.ReferenceKey == "value");
            var payload = Assert.IsType<JsonElement>(argument.Value.Value);
            Assert.Equal("Any", payload.GetProperty("resultType").GetProperty("alias").GetString());
            Assert.False(payload.GetProperty("resultType").TryGetProperty("Alias", out _));
            Assert.Equal("CapturedRequest", node.Intrinsic!.Variable!.ReferenceKey);

            // The intrinsic's authored Any target uses the same paired reader as normal activity inputs.
            var target = new InputDefinition("value", "value", new TypeReference("Any"), null, "value", null, true);
            var compiler = services.GetRequiredService<RuntimeInputBindingCompiler>();
            var compiled = compiler.Compile(node.NodeId, target, argument);
            Assert.Equal("Any", compiled.Expression!.ResultType!.Id);
            var request = Assert.IsType<ActivityResultExpressionParameterBinding>(Assert.Single(compiled.Expression.Parameters).Value);
            Assert.Equal("endpoint", request.ProducerNodeId);
            Assert.Equal("Request", request.ProjectionKey);

            var malformed = JsonNode.Parse(payload.GetRawText())!;
            var resultType = malformed["resultType"]!.AsObject();
            resultType["Alias"] = resultType["alias"]!.DeepClone();
            resultType.Remove("alias");
            var wrongCase = argument with { Value = argument.Value with { Value = JsonSerializer.SerializeToElement(malformed, JsonBaseline.Options) } };
            var refusal = Assert.Throws<ArgumentException>(() => compiler.Compile(node.NodeId, target, wrongCase));
            Assert.Contains("result type ''", refusal.Message, StringComparison.Ordinal);
            return true;
        });

        var publication = await host.PublishWorkflowAsync(versionId);
        await host.ReadAsync(async services =>
        {
            var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(publication.ArtifactId);
            Assert.NotNull(executable);
            var node = Assert.Single(executable.NodesById.Values, node => node.AuthoredActivityId == projectionId);
            Assert.Equal(WorkflowIntrinsicKind.Set, node.IntrinsicKind);
            var expression = node.InputBindings["value"].Expression!;
            Assert.Equal("Any", expression.ResultType!.Id);
            var request = Assert.IsType<ActivityResultExpressionParameterBinding>(Assert.Single(expression.Parameters).Value);
            Assert.Equal("endpoint", request.ProducerNodeId);
            Assert.Equal("Request", request.ProjectionKey);
            return true;
        });
        Assert.Equal(original, source.ToJsonString());
    }

    [Theory]
    [InlineData("String", "Elsa.WriteLine", "text", "text", "Customer", "String", "getInput('Customer') + getVariable('Greeting')")]
    [InlineData("Boolean", "Elsa.If", "condition", "Condition", "Enabled", "Boolean", "getInput('Enabled')")]
    [InlineData("Any", "Elsa.ForEach", "items", "Collection", "Items", "Object", "getInput('Items')")]
    public async Task Synthetic_lowered_expressions_publish_with_exact_aliases_parameters_and_defaults(
        string alias, string activityType, string sourceKey, string targetKey, string inputName, string sourceType, string script)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var id = "portable-json-" + alias.ToLowerInvariant();
        var subject = new JsonObject
        {
            ["id"] = "subject", ["nodeId"] = "subject", ["name"] = "subject", ["type"] = activityType, ["version"] = 1,
            [sourceKey] = Value(sourceType, "JavaScript", script)
        };
        if (alias == "Any") subject["body"] = new JsonObject
        {
            ["id"] = "loop-body", ["nodeId"] = "loop-body", ["type"] = "Elsa.WriteLine", ["version"] = 1,
            ["text"] = Value("String", "Literal", "synthetic loop body")
        };
        var source = new JsonObject
        {
            ["id"] = id + "-v1", ["definitionId"] = id, ["name"] = id, ["description"] = "Public synthetic serializer producer/reader control.",
            ["createdAt"] = "1970-01-01T00:00:00Z", ["version"] = 1, ["toolVersion"] = "3.6.0", ["isLatest"] = true, ["isPublished"] = true,
            ["options"] = new JsonObject { ["usableAsActivity"] = false },
            ["variables"] = new JsonArray(
                new JsonObject { ["id"] = "greeting", ["name"] = "Greeting", ["typeName"] = "String", ["value"] = "René<+>" },
                new JsonObject { ["id"] = "data", ["name"] = "RawData", ["typeName"] = "ObjectDictionary", ["value"] = RawDefault }),
            ["inputs"] = new JsonArray(new JsonObject { ["name"] = inputName, ["type"] = sourceType }),
            ["outputs"] = new JsonArray(), ["outcomes"] = new JsonArray(), ["customProperties"] = new JsonObject(),
            ["root"] = new JsonObject { ["id"] = "root", ["nodeId"] = "root", ["type"] = "Elsa.Sequence", ["version"] = 1, ["activities"] = new JsonArray(subject) }
        };
        var original = source.ToJsonString();
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var selected = Assert.Single(items);
        await host.SelectAsync(handle, plan, id + "-v1");
        var applied = await host.ApplyAsync(handle, plan, "import-" + id, id + "-v1");
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var versionId = Elsa3ImportHost.Required(selected, "workflowVersionId");

        await host.ReadAsync(async services =>
        {
            var version = await services.GetRequiredService<IWorkflowDefinitionVersionStore>().GetAsync(versionId);
            var node = Elsa3ImportedActivityNodes.Walk(version.State!.RootActivity!).Single(node => node.NodeId == "subject");
            var argument = Assert.Single(node.Inputs, input => input.ReferenceKey == targetKey);
            var payload = Assert.IsType<JsonElement>(argument.Value.Value);
            Assert.Equal(alias, payload.GetProperty("resultType").GetProperty("alias").GetString());
            Assert.False(payload.GetProperty("resultType").TryGetProperty("Alias", out _));
            Assert.Equal(RawDefault, Assert.Single(version.State.Variables, variable => variable.ReferenceKey == "data").Default!.Value!.Value.GetRawText());

            var catalogVersion = await services.GetRequiredService<IActivityDefinitionVersionStore>().GetAsync(node.ActivityVersionId);
            var target = Assert.Single(catalogVersion.Inputs, input => input.ReferenceKey == targetKey);
            var compiler = services.GetRequiredService<RuntimeInputBindingCompiler>();
            var compiled = compiler.Compile(node.NodeId, target, argument); // Reads DescriptorPayloadSerializer.Options, the actual paired reader.
            Assert.Equal(alias, compiled.Expression!.ResultType!.Id);
            Assert.Equal(inputName, Assert.IsType<WorkflowRequestExpressionParameterBinding>(compiled.Expression.Parameters[inputName]).MemberKey);

            var malformed = JsonNode.Parse(payload.GetRawText())!;
            var resultType = malformed["resultType"]!.AsObject();
            resultType["Alias"] = resultType["alias"]!.DeepClone();
            resultType.Remove("alias");
            var wrongCase = argument with { Value = argument.Value with { Value = JsonSerializer.SerializeToElement(malformed, JsonBaseline.Options) } };
            var refusal = Assert.Throws<ArgumentException>(() => compiler.Compile(node.NodeId, target, wrongCase));
            Assert.Contains("result type ''", refusal.Message, StringComparison.Ordinal);
            return true;
        });

        var publication = await host.PublishWorkflowAsync(versionId);
        await host.ReadAsync(async services =>
        {
            var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(publication.ArtifactId);
            Assert.NotNull(executable);
            var node = Assert.Single(executable.NodesById.Values, node => node.AuthoredActivityId == "subject");
            var expression = node.InputBindings[targetKey].Expression!;
            Assert.Equal(alias, expression.ResultType!.Id);
            Assert.Equal(inputName, Assert.IsType<WorkflowRequestExpressionParameterBinding>(expression.Parameters[inputName]).MemberKey);
            if (alias == "String") Assert.Equal("greeting", Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["variables.Greeting"]).VariableKey);
            var data = Assert.Single(executable.WorkflowVariables, variable => variable.VariableKey == "data");
            Assert.Equal("Any", data.Type.Alias);
            Assert.Equal(RawDefault, data.InitialBinding!.Literal!.InlineValue!.Value.GetRawText());
            return true;
        });
        Assert.Equal(original, source.ToJsonString());
    }

    private static JsonObject Value(string typeName, string syntax, string value) => new()
    {
        ["typeName"] = typeName, ["expression"] = new JsonObject { ["type"] = syntax, ["value"] = value }
    };
}
