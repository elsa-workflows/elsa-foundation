using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityDraftTestRunInputTests
{
    [Theory]
    [InlineData("Present", true, false, true)]
    [InlineData("Present", false, true, false)]
    [InlineData("Absent", false, false, false)]
    [InlineData("Absent", true, true, false)]
    public void Wire_input_preserves_null_versus_omitted_payload(string state, bool payload, bool refused, bool bound)
    {
        var json = "{\"expectedRevision\":1,\"idempotencyKey\":\"wire-control\",\"inputs\":{\"a\":{\"state\":\"" + state + "\"" + (payload ? ",\"value\":null" : "") + "}}}";
        var ownerOptions = typeof(ActivityDraftTestRunService).Assembly.GetType("Elsa.Workflows.Publishing.Api.WorkflowsPublishingJsonOptions")!;
        var context = (JsonSerializerContext)ownerOptions.GetProperty("WireContext", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var wireType = context.GetTypeInfo(typeof(StartActivityDraftTestRun))!;
        var request = (StartActivityDraftTestRun)JsonSerializer.Deserialize(json, wireType)!;
        var input = request.Inputs!["a"];
        Assert.Equal(payload, input.Value.HasValue);
        var roundTrip = JsonSerializer.Serialize(request, wireType);
        using var roundTripDocument = JsonDocument.Parse(roundTrip);
        Assert.Equal(payload, roundTripDocument.RootElement.GetProperty("inputs").GetProperty("a").TryGetProperty("value", out _));
        var contract = new ActivityContract("1", [new ActivityInputContract("a", "A", new TypeReference("String"), false, true, null, "elsa.json")], [], []);
        var compile = typeof(ActivityDraftTestRunService).GetMethod("CompileInputs", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (refused)
        {
            var error = Assert.Throws<TargetInvocationException>(() => compile.Invoke(null, [contract, request.Inputs]));
            Assert.Contains("Activity input 'a'", error.InnerException!.Message);
            return;
        }
        var bindings = Assert.IsAssignableFrom<IReadOnlyDictionary<string, RuntimeInputBinding>>(compile.Invoke(null, [contract, request.Inputs]));
        if (bound) Assert.Equal(ValuePresence.ExplicitNull, Assert.Single(bindings).Value.Literal!.Presence);
        else Assert.Empty(bindings);
    }

    [Theory]
    [InlineData("7", ValuePresence.Present)]
    [InlineData("null", ValuePresence.ExplicitNull)]
    public void DurableCodecDoesNotRequireExternalPayloadStorage(string json, ValuePresence presence)
    {
        var contract = new ActivityContract("1",
            [new ActivityInputContract("a", "A", new TypeReference("Int32"), true, true, null, "elsa.json")], [], []);
        var supplied = new Dictionary<string, ActivityDraftTestRunInput>
        {
            ["a"] = new("Present", JsonSerializer.Deserialize<JsonElement>(json))
        };
        // Exercise the actual wrapper compiler without exposing a production API solely for tests.
        var compile = typeof(ActivityDraftTestRunService).GetMethod("CompileInputs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var bindings = Assert.IsAssignableFrom<IReadOnlyDictionary<string, RuntimeInputBinding>>(compile.Invoke(null, [contract, supplied]));
        var binding = Assert.Single(bindings).Value;

        Assert.Equal(ValueProtectionPolicy.InstanceInline, binding.EffectivePolicy);
        Assert.Equal(presence, binding.Literal!.Presence);
        Assert.Null(binding.Literal.ExternalReference);
        Assert.Equal("Int32", binding.TargetType.Alias);
        if (presence == ValuePresence.Present)
            Assert.Equal(7, binding.Literal.InlineValue!.Value.GetInt32());
    }
}
