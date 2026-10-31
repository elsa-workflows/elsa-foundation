using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Tests;

public sealed class AgentSseContextTests
{
    [Fact]
    public void An_admitted_context_event_crosses_the_source_generated_SSE_boundary_with_its_evidence_and_limitations()
    {
        var attachment = new AgentContextAttachment("sample:r2", "Sample", "one", "Current sample", "sample.resource",
            AgentContextSensitivity.Internal, "selection", "Owner-authorized snapshot",
            JsonSerializer.SerializeToElement(new { allowedValue = "current" }), new Dictionary<string, string>())
        {
            ModuleId = "Sample", ContributorId = "sample.context", Origin = AgentContextOrigin.AuthoritativeRead,
            Target = new("Sample", "sample", "one", "r2")
        };
        var payload = new AgentContextContribution([attachment],
            [new("optional.context", "sample.optional_unavailable", "Optional evidence is unavailable.", false)]);
        var streamEvent = new AgentStreamEvent("context-event", AgentStreamEventKind.ContextAdmitted,
            null, null, null, DateTimeOffset.UtcNow) { Payload = payload };

        var json = JsonSerializer.Serialize(streamEvent, AgentSseJsonContext.Default.AgentStreamEvent);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("contextAdmitted", root.GetProperty("kind").GetString());
        var evidence = Assert.Single(root.GetProperty("payload").GetProperty("attachments").EnumerateArray());
        Assert.Equal("sample.context", evidence.GetProperty("contributorId").GetString());
        Assert.Equal("r2", evidence.GetProperty("target").GetProperty("revision").GetString());
        Assert.Equal("current", evidence.GetProperty("content").GetProperty("allowedValue").GetString());
        var limitation = Assert.Single(root.GetProperty("payload").GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("sample.optional_unavailable", limitation.GetProperty("code").GetString());
        Assert.False(limitation.GetProperty("required").GetBoolean());
    }
}
