using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Inspection.Api;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

public sealed class SpecialistInspectionApiWireTests
{
    // The endpoint's own source-generated context, not reflection: enums travel as names and the output as JSON.
    [Fact]
    public void Populated_evidence_serializes_through_the_wire_context_with_named_states_and_inline_output()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var evidence = new SpecialistInvocationEvidence("workflow-1", "activity-1",
            new("definition", "activity-test-version-abc", "draft-1", "template-hash", "artifact", "artifact-hash", true),
            new("anthropic", "model-connection", "claude-fable-5-1"), new(), "Completed", null,
            new("Payload", JsonDocument.Parse("""{"Article":"text"}""").RootElement),
            new(SpecialistTurnStatus.Accepted, null, SpecialistTurnApplicationStatus.SettlementPending, now, 8,
                [new("request-1", now, 4096, "anthropic", "claude-fable-5-1")], new(now, true, 5, 3, 8, "claude-fable-5-1"),
                new("conversation", 0, 1, false, false), null));
        var view = new SpecialistTestRunEvidenceView("run", "draft", 1, "artifact", "workflow-1", "Completed", evidence, true);

        var wire = JsonNode.Parse(JsonSerializer.Serialize(view, SpecialistInspectionApiJsonContext.WireContext.SpecialistTestRunEvidenceView))!;

        var turn = wire["invocation"]!["turn"]!;
        Assert.Equal("Accepted", (string?)turn["status"]);
        Assert.Equal("SettlementPending", (string?)turn["application"]);
        Assert.Equal(5, (long?)turn["usage"]!["inputTokens"]);
        Assert.Equal(1, (long?)turn["conversation"]!["currentRevision"]);
        Assert.Equal("Payload", (string?)wire["invocation"]!["output"]!["captureMode"]);
        Assert.Equal("text", (string?)wire["invocation"]!["output"]!["value"]!["Article"]);
        Assert.Equal("activity-test-version-abc", (string?)wire["invocation"]!["specialist"]!["definitionVersionId"]);
        Assert.True((bool)wire["valuesPermitted"]!);
        var occurrence = JsonNode.Parse(JsonSerializer.Serialize(new SpecialistInvocationEvidenceView(evidence, false), SpecialistInspectionApiJsonContext.WireContext.SpecialistInvocationEvidenceView))!;
        Assert.Equal(("Accepted", false), ((string?)occurrence["invocation"]!["turn"]!["status"], (bool)occurrence["valuesPermitted"]!));
    }
}
