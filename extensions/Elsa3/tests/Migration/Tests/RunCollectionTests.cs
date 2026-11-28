using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Migration.Tooling;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T018: how the run turns the import run's results into report definitions, without a composition.</summary>
public sealed class RunCollectionTests
{
    [Fact]
    public void A_reusable_refused_at_publish_reports_its_publish_error_once_at_the_publish_stage()
    {
        var state = new ReusableActivityPublicationState("a-activity", "a-v1", ReusableActivityPublicationStatus.Refused, "activity.publication.invalid",
            [new ActivityDiagnostic("activity.publication.invalid", ActivityDiagnosticSeverity.Error, "Elsa 4 says no", new ActivityDiagnosticSubject("activity", "a"))]);
        var skip = new Elsa3SkippedItem("activity.publication.invalid", "a.json", "a", 1, "cause", SourceVersionId: "a-v1");

        var definition = Assert.Single(Collect([Reusable("a")], skips: [skip], reusables: [state]).Definitions);

        var finding = Assert.Single(definition.Findings, finding => finding.Severity == FindingSeverity.Error);
        Assert.Equal(FindingStage.Publish, finding.Stage);
        Assert.True(finding.PublishStoppedHere);
        Assert.False(definition.Published);
    }

    [Fact]
    public void A_reusable_blocked_at_publish_by_another_names_it_in_blocked_by_and_the_other_is_the_root()
    {
        // As the publish phase reports them: the blocking dependency by its imported activity version id, with an error
        // diagnostic of its own whose guidance speaks to the upload API.
        var refused = new ReusableActivityPublicationState("a-activity", "a-v1", ReusableActivityPublicationStatus.Refused, "activity.publication.invalid",
            [new ActivityDiagnostic("activity.publication.invalid", ActivityDiagnosticSeverity.Error, "Elsa 4 says no", new ActivityDiagnosticSubject("activity", "a"))]);
        var blocked = new ReusableActivityPublicationState("b-activity", "b-v1", ReusableActivityPublicationStatus.NotPublishedDependency,
            ReusableActivityImportDiagnosticCodes.DependencyNotPublished,
            [new ActivityDiagnostic(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, ActivityDiagnosticSeverity.Error, "Version 'b-activity' was not published",
                new ActivityDiagnosticSubject("activity", "b"), Remediation: "apply it under a new idempotency key")],
            BlockedBy: "a-activity");
        var skips = new[]
        {
            new Elsa3SkippedItem("activity.publication.invalid", "a.json", "a", 1, "cause", SourceVersionId: "a-v1"),
            new Elsa3SkippedItem(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, "b.json", "b", 1, "cause", SourceVersionId: "b-v1")
        };

        var definitions = Collect([Reusable("a"), Reusable("b")], skips: skips, reusables: [refused, blocked]).Definitions;

        var b = definitions.Single(definition => definition.DefinitionId == "b");
        Assert.Equal(["a-v1"], b.BlockedBy);
        var finding = Assert.Single(b.Findings, finding => finding.Severity == FindingSeverity.Error);
        Assert.Equal(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, finding.Code);
        Assert.Equal(FindingStage.Publish, finding.Stage);
        Assert.False(finding.PublishStoppedHere);
        Assert.DoesNotContain("idempotency", finding.Guidance);

        var report = ReportBuilder.Build(new ReportInput("inventory", "0", false, definitions, [], [], [], [], [], [], true));
        Assert.Equal(["a-v1"], report.Definitions.Single(definition => definition.DefinitionId == "b").BlockedBy);
    }

    [Fact]
    public void The_same_version_twice_in_one_file_gets_one_duplicate_input_skip_each()
    {
        var first = Workflow("w");
        var second = Workflow("w");
        var skips = new[]
        {
            new Elsa3SkippedItem(Elsa3ReconciliationDiagnosticCodes.DuplicateInput, "w.json", "w", 1, "cause", SourceVersionId: "w-v1"),
            new Elsa3SkippedItem(Elsa3ReconciliationDiagnosticCodes.DuplicateInput, "w.json", "w", 1, "cause", SourceVersionId: "w-v1")
        };

        var definitions = Collect([first, second], skips: skips, withPlanItems: false).Definitions;

        Assert.All(definitions, definition => Assert.Single(definition.Findings));
    }

    [Fact]
    public void A_run_refusal_is_an_import_stage_run_finding()
    {
        var error = new Elsa3MigrationDiagnostic(Elsa3MigrationDiagnosticSeverity.Error, "VF-IMP-007", "converter message", "$.root");
        var refusal = new Elsa3ImportRunRefusal(Elsa3ImportRunStage.Apply, "VF-IMP-007", "cause", [error], ["w-v1"], new InvalidOperationException("secret"));

        var run = Collect([Workflow("w")], refusal: refusal);

        var finding = Assert.Single(run.RunFindings);
        Assert.Equal(FindingStage.Import, finding.Stage);
        Assert.Equal("converter message", finding.Message);
        Assert.Contains(run.Definitions.Single().Findings, finding => finding.Code == Elsa3MigrationToolingCodes.RunRefused);
    }

    private static ExportItem Reusable(string id) => Item(id, reusable: true);

    private static ExportItem Workflow(string id) => Item(id, reusable: false);

    private static ExportItem Item(string id, bool reusable) =>
        new($"{id}.json", new Elsa3WorkflowDefinition { Id = $"{id}-v1", DefinitionId = id, Name = id, Version = 1, IsPublished = true, IsLatest = true, Options = new() { UsableAsActivity = reusable } });

    private static CollectedRun Collect(
        IReadOnlyList<ExportItem> items,
        IReadOnlyList<Elsa3SkippedItem>? skips = null,
        IReadOnlyList<ReusableActivityPublicationState>? reusables = null,
        Elsa3ImportRunRefusal? refusal = null,
        bool withPlanItems = true)
    {
        var planItems = withPlanItems
            ? items.DistinctBy(item => item.Definition.Id).Select(item => new ReusableActivityImportItem(
                item.Definition.DefinitionId, item.Definition.Id, 1, "fingerprint", item.Definition.Options?.UsableAsActivity == true,
                $"wf-{item.Definition.DefinitionId}", $"wfv-{item.Definition.DefinitionId}", null, null, null, [], [], [], [])).ToArray()
            : [];
        var result = new Elsa3ImportRunResult(new PersistenceScope(PersistenceScope.DefaultValue), planItems, skips ?? [], reusables ?? [], [], [], refusal, []);
        return Elsa3MigrationRun.Collect(new VersionSelectionResult(items, [], []), [result]);
    }
}
