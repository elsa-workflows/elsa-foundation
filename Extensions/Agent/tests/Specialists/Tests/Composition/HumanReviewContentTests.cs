using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Agent.Specialists.Review.Activities;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Agent.Specialists.Review.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using Elsa.Activities.Sequence.Models;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>The generic review over arbitrary JSON: bounds, identity and a structured restart journey.</summary>
public sealed class HumanReviewContentTests
{
    [Theory]
    [InlineData('a', 65_534, true)]
    [InlineData('a', 65_535, false)]
    [InlineData('é', 10_922, true)]
    [InlineData('é', 10_923, false)]
    public void Content_is_bounded_by_its_canonical_encoding_including_escaping(char character, int count, bool fits)
    {
        var content = JsonSerializer.SerializeToElement(new string(character, count));

        Assert.Equal(fits, HumanReview.ContentFits(content));
    }

    [Fact]
    public void Whitespace_and_escaping_do_not_change_identity_but_key_order_does()
    {
        var compact = JsonDocument.Parse("""{"a":"é","b":[1,2]}""").RootElement;
        var pretty = JsonDocument.Parse("{\n  \"a\" : \"é\",\n  \"b\" : [ 1, 2 ]\n}").RootElement;
        var reordered = JsonDocument.Parse("""{"b":[1,2],"a":"é"}""").RootElement;

        Assert.Equal(ReviewDecisionCodec.HashContent(compact), ReviewDecisionCodec.HashContent(pretty));
        Assert.NotEqual(ReviewDecisionCodec.HashContent(compact), ReviewDecisionCodec.HashContent(reordered));
        Assert.False(HumanReview.ContentFits(JsonDocument.Parse("null").RootElement));
    }

    [Fact]
    public async Task Structured_agent_output_survives_a_restart_with_the_same_identity_and_is_approved_once()
    {
        await using var host = await EditorialHost.StartAsync("review-structured", new([], []));
        host.Subjects.Grant("reviewer-ada");
        var content = JsonSerializer.SerializeToElement(new { headline = "Moss gardens", score = 1.0, tags = new[] { "garden", "slow" }, nested = new { ok = true } });
        await host.StartRunAsync(Executable(content));
        await host.PumpAsync();

        var pending = await host.PendingReviewAsync();
        var before = Assert.IsType<HumanReviewView>(await host.ReadReviewAsync(pending.InvocationId));
        await host.RestartAsync();
        var after = Assert.IsType<HumanReviewView>(await host.ReadReviewAsync(pending.InvocationId));

        Assert.Equal(before.ContentHash, after.ContentHash);
        Assert.Equal(ReviewDecisionCodec.HashContent(content), after.ContentHash);
        Assert.Equal("Structured output", after.Title);
        Assert.Equal(JsonValueKind.Object, after.Content.ValueKind);
        Assert.Equal(content.GetProperty("score").GetRawText(), after.Content.GetProperty("score").GetRawText());

        var command = new ReviewDecisionCommand(host.WorkflowExecutionId, pending.InvocationId, after.ContentHash, ReviewDecision.Approve, null, "request-structured");
        await host.DecideAsync(command, "reviewer-ada");
        await host.PumpAsync();
        Assert.Equal(ReviewDecisionReceiptStatus.Accepted, (await host.DecideAsync(command, "reviewer-ada")).Status);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.WorkflowAsync()).Status);
        var review = Assert.Single(await host.ActivitiesAsync(), x => x.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId);
        Assert.Equal("Approved", review.Completion!.OutcomeKey);
        var resolved = Assert.IsType<HumanReviewView>(await host.ReadReviewAsync(pending.InvocationId));
        Assert.Equal(HumanReviewStatus.Approved, resolved.Status);
        Assert.Equal(before.ContentHash, resolved.ContentHash);
        Assert.Equal("Moss gardens", resolved.Content.GetProperty("headline").GetString());
    }

    private static WorkflowExecutable Executable(JsonElement content)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(HumanReview));
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Content"] = new("Content", contract.Inputs["Content"].Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(contract.Inputs["Content"].Type, content, ValueProtectionPolicy.InstanceInline)),
            ["Title"] = new("Title", contract.Inputs["Title"].Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(contract.Inputs["Title"].Type, JsonSerializer.SerializeToElement("Structured output"), ValueProtectionPolicy.InstanceInline))
        };
        var review = new ExecutableNode(EditorialHost.ReviewNodeId, "authored-review", typeof(HumanReview).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload, ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings),
            new Dictionary<string, string>(), activityContract: contract);
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var sequence = new ExecutableNode("review-sequence", "authored-review-sequence", typeof(SequenceActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, sequenceContract.DescriptorPayload, new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [review])],
            structure: new ExecutableActivityStructure(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([review.ExecutableNodeId]))),
            activityContract: sequenceContract);
        var target = new WorkflowExecutableResumeTarget(WorkflowExecutableResumeTarget.ComposeScopedId(EditorialHost.ReviewNodeId, HumanReviewIds.ResumeTargetKey),
            EditorialHost.ReviewNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), HumanReviewIds.ResumeTargetKey);
        return new WorkflowExecutable(EditorialExecutable.Identity, sequence, new Dictionary<string, WorkflowExecutableResumeTarget> { [target.ResumeTargetId] = target },
            WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), inputContract: null, dependencies: null, runtimeRequirements: null,
            storageDriverRequirements: null, IncidentStrategyBuiltIns.FaultReference);
    }
}
