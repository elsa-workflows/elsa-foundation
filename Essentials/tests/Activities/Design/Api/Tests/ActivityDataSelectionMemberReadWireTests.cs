using System.Text.Json;
using Elsa.Activities.Design.Api.Models;
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
    }
}
