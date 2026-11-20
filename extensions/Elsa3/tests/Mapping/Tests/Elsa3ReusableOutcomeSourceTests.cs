using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Elsa3FlowchartDiagnosticsTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 246 D-5 / R-9: a caller routes on the outcomes the reusable's own conversion derived, the outcomes its published
/// contract declares, rather than on a separate scan of the reusable's Elsa 3 JSON, so the two cannot disagree.
/// </summary>
public sealed class Elsa3ReusableOutcomeSourceTests
{
    [Fact]
    public async Task Caller_routes_on_exactly_the_outcomes_the_reusable_contract_declares()
    {
        var mutation = await ApplyAsync(("succeed", "outcomes"), ("fail", "outcomes"));

        var sub = Assert.Single(mutation.Activities);
        Assert.Equal(["done", "fail", "succeed"], sub.Version.Contract.Outcomes.Select(outcome => outcome.ReferenceKey));
        Assert.Equal(["call:succeed->handled", "call:fail->failed"], Support.Elsa3FlowchartFixtures.Connections(CallerRoot(mutation)));
    }

    [Fact]
    public async Task An_outcome_named_under_a_pascal_case_key_is_routed_because_the_conversion_reads_it()
    {
        // The converter reads Complete's outcomes case-insensitively; the JSON scan this replaces read only 'outcomes',
        // so a caller connection on such an outcome had no port.
        var mutation = await ApplyAsync(("succeed", "Outcomes"), ("fail", "outcomes"));

        Assert.Contains("succeed", Assert.Single(mutation.Activities).Version.Contract.Outcomes.Select(outcome => outcome.Name));
        Assert.Contains("call:succeed->handled", Support.Elsa3FlowchartFixtures.Connections(CallerRoot(mutation)));
    }

    private static Elsa.Workflows.Design.Core.Models.ActivityNode CallerRoot(ReusableActivityImportMutation mutation) =>
        mutation.Workflows.Single(workflow => workflow.Version.Id == "caller-v1").Version.State.RootActivity!;

    private static async Task<ReusableActivityImportMutation> ApplyAsync(params (string Outcome, string Key)[] completes)
    {
        var sub = Flowchart("sub-root",
            [Activity("decide", "Elsa.FlowDecision", ("condition", Expression("JavaScript", "true", "Boolean"))),
                ..completes.Select(complete => Activity($"complete-{complete.Outcome}", "Elsa.Complete", (complete.Key, Literal(complete.Outcome))))]);
        sub.Connections = completes.Select((complete, index) => Connect("decide", index == 0 ? "True" : "False", $"complete-{complete.Outcome}")).ToList();
        var call = ReusableActivityImportFixtures.Reference("call", targetVersionId: "sub-v1");
        var caller = Flowchart("caller-root", call, Write("handled"), Write("failed"));
        caller.Connections = [Connect(call.Id, "succeed", "handled"), Connect(call.Id, "fail", "failed")];
        var reusable = ReusableActivityImportFixtures.Workflow("sub", "sub-v1", 1, true, sub);
        reusable.Outcomes = completes.Select(complete => complete.Outcome).ToArray();
        var collection = ReusableActivityImportFixtures.Collection(reusable, ReusableActivityImportFixtures.Workflow("caller", "caller-v1", 1, false, caller));
        var analyzer = new ReusableActivityCollectionAnalyzer();
        var plan = await analyzer.AnalyzeAsync(collection);
        var command = new ReusableActivityImportFixtures.CapturingCommand();
        var importer = new ReusableActivityCollectionImporter(
            analyzer, ReusableActivityImportFixtures.Materializer(Support.RealActivityCatalog.Create()), command, ReusableActivityImportFixtures.Validator());

        await importer.ApplyAsync(new(plan.PlanId, collection, ["sub-v1", "caller-v1"]));

        return Assert.IsType<ReusableActivityImportMutation>(command.Mutation);
    }
}
