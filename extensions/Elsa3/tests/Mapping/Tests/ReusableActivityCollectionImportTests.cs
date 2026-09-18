using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityCollectionImportTests
{
    private readonly ReusableActivityCollectionAnalyzer analyzer = new();

    [Fact]
    public async Task Analysis_reports_deterministic_rewrites_direct_starts_missing_unsupported_and_complete_cycles()
    {
        var reusableA = ReusableActivityImportFixtures.Workflow("a", "a-v1", 1, true,
            ReusableActivityImportFixtures.Leaf("a-root", directStart: true));
        var reusableB = ReusableActivityImportFixtures.Workflow("b", "b-v1", 1, true,
            ReusableActivityImportFixtures.Reference("b-to-a", targetVersionId: "a-v1"));
        var consumer = ReusableActivityImportFixtures.Workflow("consumer", "consumer-v1", 1, false,
            ReusableActivityImportFixtures.Reference("consumer-to-b", targetDefinitionId: "b", targetVersion: 1));
        var missing = ReusableActivityImportFixtures.Workflow("missing-owner", "missing-owner-v1", 1, false,
            ReusableActivityImportFixtures.Reference("missing", targetVersionId: "absent-v1"));
        var unsupportedTrigger = ReusableActivityImportFixtures.Leaf("trigger", directStart: true);
        unsupportedTrigger.Activities = [ReusableActivityImportFixtures.Leaf("nested")];
        var unsupported = ReusableActivityImportFixtures.Workflow("unsupported", "unsupported-v1", 1, true, unsupportedTrigger);
        var cycleC = ReusableActivityImportFixtures.Workflow("c", "c-v1", 1, true, ReusableActivityImportFixtures.Reference("c-to-d", targetVersionId: "d-v1"));
        var cycleD = ReusableActivityImportFixtures.Workflow("d", "d-v1", 1, true, ReusableActivityImportFixtures.Reference("d-to-e", targetVersionId: "e-v1"));
        var cycleE = ReusableActivityImportFixtures.Workflow("e", "e-v1", 1, true, ReusableActivityImportFixtures.Reference("e-to-c", targetVersionId: "c-v1"));
        var collection = ReusableActivityImportFixtures.Collection(reusableA, reusableB, consumer, missing, unsupported, cycleC, cycleD, cycleE);

        var first = await analyzer.AnalyzeAsync(collection);
        var second = await analyzer.AnalyzeAsync(collection);

        Assert.Equal(first.PlanId, second.PlanId);
        var a = first.Items.Single(x => x.SourceVersionId == "a-v1");
        Assert.Equal("a", a.WorkflowDefinitionId);
        Assert.Equal("a-v1", a.WorkflowVersionId);
        Assert.Single(a.DirectStarts);
        Assert.NotNull(a.ActivityDefinitionId);
        Assert.NotNull(a.ActivityDefinitionVersionId);

        var bRewrite = Assert.Single(first.Items.Single(x => x.SourceVersionId == "b-v1").Rewrites);
        Assert.Equal("a-v1", bRewrite.TargetSourceVersionId);
        Assert.Equal(a.ActivityDefinitionVersionId, bRewrite.TargetActivityDefinitionVersionId);
        Assert.Equal("b-v1", Assert.Single(first.Items.Single(x => x.SourceVersionId == "consumer-v1").Rewrites).TargetSourceVersionId);

        Assert.Contains(first.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.MissingReference && x.Path == "/root");
        Assert.Contains(first.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.UnsupportedTrigger && x.Metadata["nodeId"] == "trigger");
        var cycle = Assert.Single(first.Diagnostics.Where(x => x.Code == ReusableActivityImportDiagnosticCodes.DependencyCycle));
        Assert.Equal("c-v1|d-v1|e-v1|c-v1", cycle.Metadata["cyclePath"]);
        Assert.False(first.CanApply);
    }

    [Fact]
    public async Task Analysis_handles_a_deep_dependency_chain_iteratively()
    {
        const int count = 12_000;
        var definitions = new List<Elsa3.Models.Elsa3WorkflowDefinition>(count);
        for (var index = 0; index < count; index++)
        {
            var root = index == count - 1
                ? ReusableActivityImportFixtures.Leaf($"leaf-{index}")
                : ReusableActivityImportFixtures.Reference($"ref-{index}", targetVersionId: $"v-{index + 1}");
            definitions.Add(ReusableActivityImportFixtures.Workflow($"d-{index}", $"v-{index}", 1, true, root));
        }

        var plan = await analyzer.AnalyzeAsync(new("deep", definitions));

        Assert.True(plan.CanApply);
        Assert.Equal(count, plan.Items.Count);
        Assert.Empty(plan.Diagnostics);
    }

    [Fact]
    public async Task Analysis_rejects_invalid_graph_identity_and_conflicting_exact_reference_identity()
    {
        var target = ReusableActivityImportFixtures.Workflow(
            "target",
            "target-v1",
            1,
            true,
            ReusableActivityImportFixtures.Leaf("target-root"));
        var reference = ReusableActivityImportFixtures.Reference("reference", targetVersionId: "target-v1");
        reference.AdditionalProperties["workflowDefinitionId"] = System.Text.Json.JsonSerializer.SerializeToElement("different-target");
        var duplicateRoot = ReusableActivityImportFixtures.Leaf("duplicate");
        duplicateRoot.Activities = [ReusableActivityImportFixtures.Leaf("duplicate"), reference];
        var owner = ReusableActivityImportFixtures.Workflow("owner", "owner-v1", 1, true, duplicateRoot);
        var missingRoot = ReusableActivityImportFixtures.Workflow("missing-root", "missing-root-v1", 1, true, null!);

        var plan = await analyzer.AnalyzeAsync(ReusableActivityImportFixtures.Collection(target, owner, missingRoot));

        Assert.Contains(plan.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.DuplicateNodeId && x.Path == "/root/activities/0");
        Assert.Contains(plan.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.UnsupportedReference && x.Metadata["nodeId"] == "reference");
        Assert.Contains(plan.Diagnostics, x => x.Code == ReusableActivityImportDiagnosticCodes.InvalidSource && x.Path == "/root");
    }

    [Fact]
    public async Task Analysis_only_inventories_direct_starts_for_reusable_sources()
    {
        var ordinary = ReusableActivityImportFixtures.Workflow(
            "ordinary",
            "ordinary-v1",
            1,
            false,
            ReusableActivityImportFixtures.Leaf("ordinary-trigger", directStart: true));

        var plan = await analyzer.AnalyzeAsync(ReusableActivityImportFixtures.Collection(ordinary));

        Assert.Empty(Assert.Single(plan.Items).DirectStarts);
    }
}
