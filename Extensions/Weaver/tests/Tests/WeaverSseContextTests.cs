using System.Text.Json;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Tests;

public sealed class WeaverSseContextTests
{
    [Fact]
    public void An_admitted_context_event_crosses_the_source_generated_SSE_boundary_with_its_evidence_and_limitations()
    {
        var attachment = new WeaverContextAttachment("sample:r2", "Sample", "one", "Current sample", "sample.resource",
            WeaverContextSensitivity.Internal, "selection", "Owner-authorized snapshot",
            JsonSerializer.SerializeToElement(new { allowedValue = "current" }), new Dictionary<string, string>())
        {
            ModuleId = "Sample", ContributorId = "sample.context", Origin = WeaverContextOrigin.AuthoritativeRead,
            Target = new("Sample", "sample", "one", "r2")
        };
        var payload = new WeaverContextContribution([attachment],
            [new("optional.context", "sample.optional_unavailable", "Optional evidence is unavailable.", false)]);
        var streamEvent = new WeaverStreamEvent("context-event", WeaverStreamEventKind.ContextAdmitted,
            null, null, null, DateTimeOffset.UtcNow) { Payload = payload };

        var json = JsonSerializer.Serialize(streamEvent, WeaverSseJsonContext.Default.WeaverStreamEvent);

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
