using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class AlterationActivityRevisionTests
{
    [Fact]
    public void Captured_revision_uses_exact_json_values_and_validates_the_same_projection()
    {
        var activity = Activity("""{"n":100000000000000000000000000000000000001.0,"text":"\u0061","nil":null,"items":[1,2]}""");
        var revisions = WorkflowAlterationCapturedConcurrencyValidator.CaptureActivities([activity]);
        var job = new WorkflowAlterationJobState("job", "plan", "workflow", "tenant", 0,
            WorkflowAlterationJobStatus.Pending, null, 0, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
            new WorkflowAlterationCapturedConcurrency(null, null, null, revisions));
        var same = Activity("""{"items":[1e0,2.0],"nil":null,"text":"a","n":100000000000000000000000000000000000001}""");
        Assert.Null(WorkflowAlterationCapturedConcurrencyValidator.ValidateActivity(job, same, "target"));
        Assert.Equal(revisions["activity"], WorkflowAlterationCapturedConcurrencyValidator.CaptureActivities([same])["activity"]);
        foreach (var changed in new[]
                 {
                     """{"n":100000000000000000000000000000000000002,"text":"a","nil":null,"items":[1,2]}""",
                     """{"n":100000000000000000000000000000000000001,"text":"a","items":[1,2]}""",
                     """{"n":100000000000000000000000000000000000001,"text":"a","nil":null,"items":[2,1]}""",
                     """{"N":100000000000000000000000000000000000001,"text":"a","nil":null,"items":[1,2]}"""
                 })
            Assert.Equal("ActivityStateRevisionConflict", WorkflowAlterationCapturedConcurrencyValidator.ValidateActivity(job, Activity(changed), "target")?.Code);
    }

    private static ActivityExecutionState Activity(string json) => new(
        new ActivityExecution("activity", "workflow", "node", "node", "activity-type", "1"),
        ActivityExecutionStatus.Suspended, null, 0, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
        ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, null), null, [], [], 0, 0,
        new Dictionary<string, string>(), PrivateState: new ActivityPrivateState("activity", 1,
            ValueEnvelope.Inline(new ValueTypeDescriptor("Object"), JsonDocument.Parse(json).RootElement.Clone(), ValueProtectionPolicy.InstanceInline),
            "attempt", DateTimeOffset.UnixEpoch));
}
