using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Weaver.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Authoring;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Weaver.Dashboard.Tests;

public sealed class DashboardQuestionGrammarTests
{
    private const string Candidate = """
        {"question":{"query":{"schemaVersion":1,"dataset":{"id":"fixture.records","schemaRevision":"1"},
          "form":"aggregate","groupBy":["id"],"filters":{"field":"id","operator":"in","values":[{"type":"string","literal":"one"},{"type":"string","literal":"two"}]},
          "metrics":[{"alias":"rate","metric":"fixture.rate","revision":"started-current-status-v1"}],
          "time":{"field":"startedAt","window":"calendarWeekToDate","timeZone":"UTC","weekStart":"monday"},
          "comparison":{"mode":"previousCalendarWeekSameElapsed"},
          "having":{"metric":"delta.rate","operator":"gt","value":{"type":"decimal","literal":"0"}},
          "sort":[{"reference":"delta.rate","direction":"descending","nulls":"last"}],"limit":50},
          "visualization":{"kind":"bar","x":"id","y":["current.rate","baseline.rate","delta.rate"]},"parameterValues":{}}}
        """;

    [Theory]
    [InlineData("numericLiteral", "$.question.query.having.value.literal")]
    [InlineData("arrayLiteral", "$.question.query.filters.values[].literal")]
    [InlineData("numericWeekStart", "$.question.query.time.weekStart")]
    [InlineData("unknownMember", "$.question.query.time")]
    [InlineData("parameterName", "$.question.parameterValues")]
    public async Task Invalid_wire_arguments_receive_safe_correction_without_execution_or_persistence(string change, string location)
    {
        await using var host = new DashboardWeaverHost(new() { TrendGrammar = true });
        await host.InitializeAsync();
        var args = JsonNode.Parse(Candidate)!;
        var query = args["question"]!["query"]!;
        const string secret = "private-property-canary-72d8";
        switch (change)
        {
            case "numericLiteral": query["having"]!["value"]!["literal"] = 0; break;
            case "arrayLiteral": query["filters"]!["values"]![0]!["literal"] = new JsonArray(secret); break;
            case "numericWeekStart": query["time"]!["weekStart"] = 1; break;
            case "unknownMember": query["time"]![secret] = secret; break;
            case "parameterName": args["question"]!["parameterValues"]![secret] = new JsonObject { ["type"] = "integer", ["literal"] = 0 }; break;
        }
        var tool = (await host.Services.GetRequiredService<IWeaverToolRegistry>().FindAsync("dashboard_preview_question"))!;
        var result = await tool.PrepareAsync(await host.InvocationAsync(tool.Descriptor.Name, args, DashboardWeaverHost.Draft()));
        Assert.False(result.Succeeded);
        Assert.Equal("dashboards.authoring.invalid", result.Error!.Code);
        Assert.Contains(location, result.Error.Message);
        Assert.Contains("JSON strings", result.Error.Message);
        Assert.Contains("values:[{type,literal}]", result.Error.Message);
        Assert.DoesNotContain(secret, result.Error.Message);
        Assert.Equal(0, host.Source.ExecutionCalls);
        Assert.Empty((await host.Resources.ListAsync(host.Actors.Actor, "question")).Items);
    }

    [Fact]
    public async Task Corrected_weekly_IN_comparison_and_delta_having_produces_an_unsaved_private_proposal()
    {
        await using var host = new DashboardWeaverHost(new() { TrendGrammar = true });
        await host.InitializeAsync();
        var tool = (await host.Services.GetRequiredService<IWeaverToolRegistry>().FindAsync("dashboard_preview_question"))!;
        var invocation = await host.InvocationAsync(tool.Descriptor.Name, JsonNode.Parse(Candidate)!, DashboardWeaverHost.Draft());
        var prepared = await host.PrepareAsync(tool, invocation);
        var result = await tool.ExecuteAsync(prepared);
        Assert.True(result.Succeeded, result.Error?.Message);
        var proposal = result.Value!.Payload!.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json)!;
        Assert.Equal("delta.rate", proposal.Definition.Query.Having!.Metric);
        Assert.Equal(2, proposal.Definition.Query.Filters!.Values!.Count);
        Assert.Null(proposal.Preview);
        Assert.DoesNotContain(FixtureSource.RowCanary, result.Value.Payload.Data.GetRawText());
        Assert.Equal(0, host.Source.ExecutionCalls);
        Assert.Empty((await host.Resources.ListAsync(host.Actors.Actor, "question")).Items);
    }

    [Fact]
    public async Task Advertised_schema_explains_the_previously_opaque_nested_grammar()
    {
        await using var host = new DashboardWeaverHost();
        var tool = (await host.Services.GetRequiredService<IWeaverToolRegistry>().FindAsync("dashboard_preview_question"))!;
        using var document = JsonDocument.Parse(tool.Descriptor.JsonSchema);
        var root = document.RootElement;
        var defs = root.GetProperty("$defs");
        Assert.Equal("string", defs.GetProperty("value").GetProperty("properties").GetProperty("literal").GetProperty("type").GetString());
        Assert.Equal("#/$defs/value", defs.GetProperty("predicate").GetProperty("properties").GetProperty("values").GetProperty("items").GetProperty("$ref").GetString());
        Assert.True(defs.GetProperty("having").GetProperty("properties").TryGetProperty("metric", out _));
        Assert.False(defs.GetProperty("having").GetProperty("properties").TryGetProperty("field", out _));
        var query = root.GetProperty("properties").GetProperty("question").GetProperty("properties").GetProperty("query").GetProperty("properties");
        Assert.Equal("monday", query.GetProperty("time").GetProperty("properties").GetProperty("weekStart").GetProperty("const").GetString());
        Assert.False(query.GetProperty("metrics").GetProperty("items").GetProperty("additionalProperties").GetBoolean());
    }
}
