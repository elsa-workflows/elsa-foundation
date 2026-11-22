using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeArtifactJsonDurableValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_inline_and_external_rows_keep_the_persistence_profile_and_opaque_utf16(bool external)
    {
        var metadata = new Dictionary<string, string> { ["key-\ud802"] = "value-\ud803" };
        var value = new DurableValueState("id-\ud800", "workflow-\ud801", "output:Value", new("Any", "Single", null),
            DurableValueLifecycle.Instance, external ? DurableValueStorage.External : DurableValueStorage.Inline,
            external ? null : JsonSerializer.SerializeToElement(42), external ? new("store-\ud804", "locator-\ud805", metadata) : null,
            "body-\ud806", DateTimeOffset.UnixEpoch, metadata);
        var json = Invoke<string>("Serialize", value);
        var restored = Invoke<DurableValueState>("Deserialize", json);
        Assert.Equal(value.DurableValueId, restored.DurableValueId);
        Assert.Equal(value.WorkflowExecutionId, restored.WorkflowExecutionId);
        Assert.Equal(value.SourceActivityExecutionId, restored.SourceActivityExecutionId);
        Assert.Equal(metadata["key-\ud802"], restored.Metadata["key-\ud802"]);
        if (external) Assert.Equal(value.ExternalReference!.Locator, restored.ExternalReference!.Locator);
        else Assert.Equal(42, restored.InlineValue!.Value.GetInt32());
        Assert.Equal(json, Invoke<string>("Serialize", restored));
    }

    // Exercise the actual EF serialization profile without adding an internals grant.
    private static T Invoke<T>(string name, object argument) => (T)typeof(EfDurableValueStateStore).Assembly
        .GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimeArtifactJson", throwOnError: true)!
        .GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method => method.Name == name)
        .MakeGenericMethod(typeof(DurableValueState)).Invoke(null, [argument])!;
}
