using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>The activity-definition endpoint's wire carries whether member reads are on; an answer without it reads as off.</summary>
public sealed class ActivityDataSelectionMemberReadWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_answer_carries_whether_member_reads_are_on(bool memberReads)
    {
        var response = new ActivityDataSelectionContextResponse("1", "empty", null, "ActivityDefinition", "draft", "1", "document", "hash", "context",
            new ActivityDataSelectionAnalysis("hash", null, [], []) { MemberReadsEnabled = memberReads });

        var json = JsonSerializer.Serialize(response, ActivitiesDesignJsonOptions.WireContext.ActivityDataSelectionContextResponse);
        var read = JsonSerializer.Deserialize(json, ActivitiesDesignJsonOptions.WireContext.ActivityDataSelectionContextResponse);

        Assert.Contains($"\"memberReadsEnabled\":{(memberReads ? "true" : "false")}", json, StringComparison.Ordinal);
        Assert.Equal(memberReads, read!.Analysis!.MemberReadsEnabled);
    }

    [Fact]
    public void An_answer_without_the_member_reads_fact_reads_as_off()
    {
        var read = JsonSerializer.Deserialize(
            """{"contractVersion":"1","state":"empty","resourceKind":"ActivityDefinition","resourceId":"draft","documentRevision":"document","analysis":{"contractRevision":"hash","sources":[],"findings":[]}}""",
            ActivitiesDesignJsonOptions.WireContext.ActivityDataSelectionContextResponse);

        Assert.False(read!.Analysis!.MemberReadsEnabled);
        Assert.False(read.Analysis.ExpressionParametersEnabled);
    }
    [Theory]
    [InlineData("request")]
    [InlineData("variable")]
    [InlineData("result")]
    public void The_activity_definition_wire_retains_portable_query_kinds_without_authored_member_fields(string role)
    {
        ExpressionParameterBinding parameter = role switch
        {
            "request" => new WorkflowRequestExpressionParameterBinding("DeclaredName"),
            "variable" => new VariableExpressionParameterBinding("scope-case", "VariableKey"),
            _ => new ActivityResultExpressionParameterBinding("producer", "$result", true)
        };
        var request = new ActivityDataSelectionContextRequest("draft", "1", 5, "edit-7", new("consumer", "value"),
            new() { ExpressionType = "JavaScript", ParameterSource = parameter, MemberPath = ["a.b", ""] });
        var json = JsonSerializer.Serialize(request, ActivitiesDesignJsonOptions.WireContext.ActivityDataSelectionContextRequest);
        var read = JsonSerializer.Deserialize(json, ActivitiesDesignJsonOptions.WireContext.ActivityDataSelectionContextRequest);
        Assert.Equal(parameter, read!.Query!.ParameterSource); Assert.Equal("JavaScript", read.Query.ExpressionType);
        Assert.Equal(new[] { "a.b", "" }, read.Query.MemberPath.ToArray());
        using var wire = JsonDocument.Parse(json);
        Assert.False(wire.RootElement.GetProperty("query").GetProperty("parameterSource").TryGetProperty("memberPath", out _));
    }

}
