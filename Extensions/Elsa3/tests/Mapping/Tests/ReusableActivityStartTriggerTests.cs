using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 246 D-6 refuses a reusable workflow that starts from a trigger. Elsa 3 also lets an engineer tick 'Can start
/// workflow' on a non-trigger, where it only picks the flowchart's start node, and conversion keeps that (spec 248); the
/// analyzer asks the activity map which flagged activities are triggers, so only real triggers are refused.
/// </summary>
public sealed class ReusableActivityStartTriggerTests
{
    private readonly ReusableActivityCollectionAnalyzer analyzer = new(new Elsa3ReusableActivityTriggerClassifier(new Elsa3BuiltInActivityMap()));

    [Theory]
    [InlineData("Elsa.WriteLine")]
    [InlineData("Elsa.If")]
    [InlineData("Elsa.Flowchart")]
    public async Task A_flagged_non_trigger_is_not_a_start_trigger(string type)
    {
        var root = ReusableActivityImportFixtures.Leaf("root", "Elsa.Flowchart");
        root.Activities = [ReusableActivityImportFixtures.Leaf("start", type, directStart: true)];
        var reusable = ReusableActivityImportFixtures.Workflow("reusable", "reusable-v1", 1, true, root);

        var plan = await analyzer.AnalyzeAsync(ReusableActivityImportFixtures.Collection(reusable));

        var item = Assert.Single(plan.Items);
        Assert.Empty(item.DirectStarts);
        Assert.Empty(item.Diagnostics);
        Assert.True(plan.CanApply);
    }

    [Theory]
    [InlineData("Elsa.HttpEndpoint")]
    [InlineData("Elsa.Timer")]
    [InlineData("Elsa.Event")]
    public async Task A_flagged_trigger_is_refused_without_the_non_trigger_hint(string type)
    {
        var reusable = ReusableActivityImportFixtures.Workflow("reusable", "reusable-v1", 1, true,
            ReusableActivityImportFixtures.Leaf("trigger", type, directStart: true));

        var plan = await analyzer.AnalyzeAsync(ReusableActivityImportFixtures.Collection(reusable));

        var diagnostic = Assert.Single(Assert.Single(plan.Items).Diagnostics);
        Assert.Equal(ReusableActivityImportDiagnosticCodes.UnsupportedStartTrigger, diagnostic.Code);
        Assert.DoesNotContain("is not a trigger", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_flagged_activity_of_an_unknown_type_is_refused_and_told_how_to_clear_the_flag()
    {
        var reusable = ReusableActivityImportFixtures.Workflow("reusable", "reusable-v1", 1, true,
            ReusableActivityImportFixtures.Leaf("custom", "Acme.SendInvoice", directStart: true));

        var plan = await analyzer.AnalyzeAsync(ReusableActivityImportFixtures.Collection(reusable));

        var diagnostic = Assert.Single(Assert.Single(plan.Items).Diagnostics);
        Assert.Equal(ReusableActivityImportDiagnosticCodes.UnsupportedStartTrigger, diagnostic.Code);
        Assert.Contains("If 'Acme.SendInvoice' is not a trigger, clear 'Can start workflow'", diagnostic.Guidance, StringComparison.Ordinal);
    }
}
