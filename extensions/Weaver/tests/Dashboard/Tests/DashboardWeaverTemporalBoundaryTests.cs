using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Weaver.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Weaver.Dashboard.Tests;

public sealed class DashboardWeaverTemporalBoundaryTests
{
    [Theory]
    [InlineData("time", "from")]
    [InlineData("time", "to")]
    [InlineData("comparison", "from")]
    [InlineData("comparison", "to")]
    public void Draft_reads_refuse_an_offsetless_query_instant(string owner, string member)
    {
        var attachment = DashboardWeaverProtocol.ClientAttachment(DashboardWeaverHost.Draft(DashboardWeaverHost.Question()));
        var content = JsonNode.Parse(attachment.Content!.Value.GetRawText())!.AsObject();
        content["definition"] = MalformedQuestion(owner, member);
        var raw = JsonSerializer.SerializeToElement(content, JsonBaseline.Options);
        var error = Assert.Throws<JsonConversionException>(() => DashboardWeaverProtocol.ReadDraft(attachment with { Content = raw }));
        Assert.Equal("json-datetime-offset-missing", error.Code);
    }

    [Theory]
    [InlineData("time", "from")]
    [InlineData("time", "to")]
    [InlineData("comparison", "from")]
    [InlineData("comparison", "to")]
    public async Task Actual_question_tool_refuses_an_offsetless_query_instant(string owner, string member)
    {
        await using var host = new DashboardWeaverHost();
        var tool = await host.Services.GetRequiredService<IWeaverToolRegistry>().FindAsync("dashboard_preview_question");
        var question = JsonSerializer.SerializeToElement(MalformedQuestion(owner, member), JsonBaseline.Options);
        var invocation = await host.InvocationAsync(tool!.Descriptor.Name, new { question });
        var result = await tool.PrepareAsync(invocation);
        Assert.False(result.Succeeded);
        Assert.Equal("dashboards.authoring.invalid", result.Error!.Code);
        Assert.Equal(422, result.Error.StatusCode);
        Assert.Equal(0, host.Source.ExecutionCalls);
    }

    [Fact]
    public void Explicit_offset_survives_producer_wire_reader_and_fingerprint()
    {
        var from = DateTimeOffset.Parse("2026-10-28T09:15:00+05:45", System.Globalization.CultureInfo.InvariantCulture);
        var to = from.AddHours(1);
        var source = DashboardWeaverHost.Question();
        var question = source with { Query = source.Query with
        {
            Time = new() { Field = "startedAt", From = from, To = to },
            Comparison = new("custom", from.AddDays(-1), to.AddDays(-1))
        } };
        var attachment = DashboardWeaverProtocol.ClientAttachment(DashboardWeaverHost.Draft(question));
        var wire = JsonSerializer.Serialize(attachment, DashboardWeaverProtocol.Json);
        var received = JsonSerializer.Deserialize<Elsa.Weaver.Core.Models.WeaverContextAttachment>(wire, DashboardWeaverProtocol.Json)!;
        var actual = DashboardWeaverProtocol.ReadDraft(received).Definition!;
        Assert.Equal(from.Offset, actual.Query.Time!.From!.Value.Offset);
        Assert.Equal(from.UtcTicks, actual.Query.Time.From.Value.UtcTicks);
        Assert.Equal(to.Offset, actual.Query.Time.To!.Value.Offset);
        Assert.Equal(question.Query.Comparison, actual.Query.Comparison);
        Assert.Equal(DashboardWeaverProtocol.Fingerprint(question), DashboardWeaverProtocol.Fingerprint(actual));
    }

    private static JsonObject MalformedQuestion(string owner, string member)
    {
        var question = JsonSerializer.SerializeToNode(DashboardWeaverHost.Question(), DashboardWeaverProtocol.Json)!.AsObject();
        question["query"]![owner] = owner == "time"
            ? new JsonObject { ["field"] = "startedAt", ["from"] = "2026-10-28T08:15:00Z", ["to"] = "2026-10-28T10:15:00Z" }
            : new JsonObject { ["mode"] = "custom", ["from"] = "2026-10-27T08:15:00Z", ["to"] = "2026-10-27T10:15:00Z" };
        question["query"]![owner]![member] = "2026-10-28T09:15:00";
        return question;
    }
}
