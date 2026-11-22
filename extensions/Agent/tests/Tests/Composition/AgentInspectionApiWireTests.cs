using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Inspection.Api;
using Xunit;

namespace Elsa.Agent.Tests.Composition;

public sealed class AgentInspectionApiWireTests
{
    // The endpoint's own source-generated context, not reflection: enums travel as names and the output as JSON.
    [Fact]
    public void Populated_evidence_serializes_through_the_wire_context_with_named_states_and_inline_output()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var evidence = new AgentInvocationEvidence("workflow-1", "activity-1",
            new("definition", "activity-test-version-abc", "draft-1", "template-hash", "artifact", "artifact-hash", true),
            new("anthropic", "model-connection", "claude-fable-5-1"), new(), "Completed", null,
            new("Payload", JsonDocument.Parse("""{"Article":"text"}""").RootElement),
            new(AgentTurnStatus.Accepted, null, AgentTurnApplicationStatus.SettlementPending, now, 8,
                [new("request-1", now, 4096, "anthropic", "claude-fable-5-1")], new(now, true, 5, 3, 8, "claude-fable-5-1"),
                new("conversation", 0, 1, false, false), null));
        var view = new AgentTestRunEvidenceView("run", "draft", 1, "artifact", "workflow-1", "Completed", evidence, true);

        var wire = JsonNode.Parse(JsonSerializer.Serialize(view, AgentInspectionApiJsonContext.WireContext.AgentTestRunEvidenceView))!;

        var turn = wire["invocation"]!["turn"]!;
        Assert.Equal("Accepted", (string?)turn["status"]);
        Assert.Equal("SettlementPending", (string?)turn["application"]);
        Assert.Equal(5, (long?)turn["usage"]!["inputTokens"]);
        Assert.Equal(1, (long?)turn["conversation"]!["currentRevision"]);
        Assert.Equal("Payload", (string?)wire["invocation"]!["output"]!["captureMode"]);
        Assert.Equal("text", (string?)wire["invocation"]!["output"]!["value"]!["Article"]);
        Assert.Equal("activity-test-version-abc", (string?)wire["invocation"]!["agent"]!["definitionVersionId"]);
        Assert.True((bool)wire["valuesPermitted"]!);
        var occurrence = JsonNode.Parse(JsonSerializer.Serialize(new AgentInvocationEvidenceView(evidence, false), AgentInspectionApiJsonContext.WireContext.AgentInvocationEvidenceView))!;
        Assert.Equal(("Accepted", false), ((string?)occurrence["invocation"]!["turn"]!["status"], (bool)occurrence["valuesPermitted"]!));
    }
}
