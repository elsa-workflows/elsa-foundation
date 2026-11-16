using System.Reflection;
using System.Text.Json;
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
