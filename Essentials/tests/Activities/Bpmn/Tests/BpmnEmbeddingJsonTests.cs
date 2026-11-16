using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnEmbeddingJsonTests
{
    [Fact]
    public void Opaque_native_state_retains_the_exact_model_pin_even_for_unpaired_identity_code_units()
    {
        foreach (var text in new[] { "plain", "opaque-\ud800", "opaque-\udc00", "𝄞" })
        {
            var pin = new BpmnEmbeddingPin(text, new("artifact", text, "version", "1.0.0", "hash"), "incarnation");
            var state = new RunBpmnState(1, BpmnEmbeddingJson.Write(pin));
            var persisted = JsonSerializer.Deserialize<RunBpmnState>(JsonSerializer.Serialize(state))!;
            Assert.Equal(pin, BpmnEmbeddingJson.Read<BpmnEmbeddingPin>(persisted.Invocation));
        }
    }

    [Fact]
    public void New_invocation_documents_refuse_unencoded_oversized_and_future_state()
    {
        Assert.Throws<BpmnModelValidationException>(() => BpmnEmbeddingJson.Read<BpmnEmbeddingPin>("{\"sourceReferenceId\":\"unencoded\"}"));
        Assert.Throws<BpmnModelValidationException>(() => BpmnEmbeddingJson.Read<BpmnEmbeddingPin>(new string(' ', 65537)));
        Assert.Throws<BpmnModelValidationException>(() => BpmnEmbeddingBoundary.Read(new RunBpmnState(2, JsonSerializer.SerializeToElement(new { }))));
    }
}
