using Elsa.Activities.Design.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec 235 research R-5: which input items one start imports, and why the others are skipped.</summary>
public sealed class Elsa3ImportSelectionTests
{
    private readonly Elsa3ImportSelection _selection = new();

    [Fact]
    public void Blank_and_duplicate_source_ids_and_duplicate_versions_are_skipped_before_analysis()
    {
        var items = new[]
        {
            Input("a", "a-v1", 1, "one.json"),
            Input("b", "", 1, "two.json"),
            Input("c", "c-v1", 1, "three.json"),
            Input("c", "c-v1", 1, "four.json"),
            Input("d", "d-v1", 1, "five.json"),
            Input("d", "d-v1-copy", 1, "six.json")
        };

        var (kept, skipped) = _selection.SelectInput(items);

        Assert.Equal(["a-v1"], kept.Select(item => item.Definition.Id));
        Assert.Equal(5, skipped.Count);
        Assert.All(skipped, item => Assert.Equal(Elsa3ReconciliationDiagnosticCodes.DuplicateInput, item.Code));
        Assert.Equal(["five.json", "four.json", "six.json", "three.json", "two.json"], skipped.Select(item => item.FilePath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_item_with_an_error_is_skipped_with_every_item_that_uses_it_transitively()
    {
        // c uses b, b uses a; a fails; d is independent.
        var plan = Plan(
            Item("a", error: "ELS3-TEST-ERROR"),
            Item("b", dependsOn: "a"),
            Item("c", dependsOn: "b"),
            Item("d"));

        var (selected, skipped) = _selection.SelectPlan(plan, Inputs(plan));

        Assert.Equal(["d"], selected);
        var a = Assert.Single(skipped, item => item.DefinitionId == "a");
        Assert.Equal("ELS3-TEST-ERROR", a.Code);
        Assert.Null(a.BlockedBy);
        var b = Assert.Single(skipped, item => item.DefinitionId == "b");
        Assert.Equal(Elsa3ReconciliationDiagnosticCodes.BlockedByDependency, b.Code);
        Assert.Equal("a", b.BlockedBy);
        Assert.Equal("b", Assert.Single(skipped, item => item.DefinitionId == "c").BlockedBy);
    }

    [Fact]
    public void Cycle_members_and_their_dependents_are_skipped()
    {
        var plan = Plan(
            Item("x", error: "ELS3-REUSABLE-DEPENDENCY-CYCLE", dependsOn: "y"),
            Item("y", error: "ELS3-REUSABLE-DEPENDENCY-CYCLE", dependsOn: "x"),
            Item("caller", dependsOn: "x"),
            Item("free"));

        var (selected, skipped) = _selection.SelectPlan(plan, Inputs(plan));

        Assert.Equal(["free"], selected);
        Assert.Equal(["caller", "x", "y"], skipped.Select(item => item.DefinitionId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_refusal_is_located_by_metadata_then_by_path_segment()
    {
        var exception = new ReusableActivityImportValidationException("refused",
        [
            Error("ELS3-A", metadata: new Dictionary<string, string> { ["SourceVersionId"] = "a" }),
            Error("ELS3-B", pathSegments: [new(Elsa3MigrationPathSegmentKind.SourceVersion, "b")])
        ]);

        var located = Elsa3FolderImportTask.Locate(exception, ["a", "b", "c"]);

        Assert.Equal(["a", "b"], located.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("ELS3-A", located["a"].Code);
        Assert.Equal("ELS3-B", located["b"].Code);
    }

    [Fact]
    public void A_refusal_with_an_error_that_names_no_selected_item_is_not_located()
    {
        var exception = new ReusableActivityImportValidationException("refused",
        [
            Error("ELS3-A", metadata: new Dictionary<string, string> { ["SourceVersionId"] = "a" }),
            Error("ELS3-COLLECTION")
        ]);

        Assert.Empty(Elsa3FolderImportTask.Locate(exception, ["a"]));
    }

    [Theory]
    [InlineData("schema-write-refused")]
    [InlineData("schema-version-not-finalized")]
    public void A_refused_reusable_is_skipped_with_what_its_first_error_says(string refusalCode)
    {
        // #5972: a schema refusal names its family and versions only in its diagnostic; the skip must keep them. The
        // remediation speaks to an upload and an idempotency key, which an engine start has neither of, so it is left out.
        var state = new ReusableActivityPublicationState("v1", "a", ReusableActivityPublicationStatus.Refused, refusalCode,
        [
            new("activity.note", ActivityDiagnosticSeverity.Warning, "A warning first.", Subject()),
            new(refusalCode, ActivityDiagnosticSeverity.Error,
                "Schema family 'activity-drafts' requires version 3; this host writes 2.", Subject(),
                Remediation: "Upload the same export again and apply it under a new idempotency key.")
        ]);

        var (code, cause) = Elsa3FolderImportTask.NotPublished(state);

        Assert.Equal(refusalCode, code);
        Assert.Equal("The reusable activity was not published (Refused): Schema family 'activity-drafts' requires version 3; this host writes 2. The next start tries again.", cause);
    }

    [Fact]
    public void A_refusal_a_retry_cannot_fix_keeps_its_remediation_and_promises_no_retry()
    {
        // A collision comes back on every start; its remediation is the only advice the operator gets.
        var state = new ReusableActivityPublicationState("v1", "a", ReusableActivityPublicationStatus.Refused, "ELS3-REUSABLE-IDENTITY-COLLISION",
        [
            new("ELS3-REUSABLE-IDENTITY-COLLISION", ActivityDiagnosticSeverity.Error, "Version id 'v1' is published from other content.", Subject(),
                Remediation: "Publish the changed workflow as a new version in Elsa 3, re-export, and import that version.")
        ]);

        var (_, cause) = Elsa3FolderImportTask.NotPublished(state);

        Assert.Equal("The reusable activity was not published (Refused): Version id 'v1' is published from other content. " +
                     "Publish the changed workflow as a new version in Elsa 3, re-export, and import that version.", cause);
    }

    [Fact]
    public void An_unexpected_publish_failure_points_to_the_log_and_the_next_start()
    {
        var state = new ReusableActivityPublicationState("v1", "a", ReusableActivityPublicationStatus.Refused, "activity.publication.failed",
        [
            new("activity.publication.failed", ActivityDiagnosticSeverity.Error, "Publishing the imported activity version 'v1' failed unexpectedly.", Subject(),
                Remediation: "To retry, upload the same export again.")
        ]);

        var (_, cause) = Elsa3FolderImportTask.NotPublished(state);

        Assert.Equal("The reusable activity was not published (Refused): Publishing the imported activity version 'v1' failed unexpectedly. " +
                     "The error logged before this one has the cause; the next start tries again.", cause);
    }

    [Fact]
    public void A_refused_reusable_without_an_error_diagnostic_names_its_status_only()
    {
        var warningOnly = new ReusableActivityPublicationState("v1", "a", ReusableActivityPublicationStatus.Refused, "code",
            [new("activity.note", ActivityDiagnosticSeverity.Warning, "A warning.", Subject(), Remediation: "Do something.")]);
        var none = new ReusableActivityPublicationState("v1", "a", ReusableActivityPublicationStatus.Refused);

        Assert.Equal("The reusable activity was not published (Refused).", Elsa3FolderImportTask.NotPublished(warningOnly).Cause);
        Assert.Equal(("Refused", "The reusable activity was not published (Refused)."), Elsa3FolderImportTask.NotPublished(none));
    }

    [Fact]
    public void A_reusable_blocked_by_a_dependency_names_the_status_and_the_dependency_only()
    {
        // The phase attaches a diagnostic that repeats the dependency with upload advice; the dependency's own skip says why.
        var state = new ReusableActivityPublicationState("v2", "b", ReusableActivityPublicationStatus.NotPublishedDependency,
            "ELS3-REUSABLE-DEPENDENCY-NOT-PUBLISHED",
            [
                new("ELS3-REUSABLE-DEPENDENCY-NOT-PUBLISHED", ActivityDiagnosticSeverity.Error,
                    "Version 'v2' was not published because the version it uses, 'v1', was not published.", Subject(),
                    Remediation: "Resolve why 'v1' was not published, then upload the same export again.")
            ],
            BlockedBy: "v1");

        var (code, cause) = Elsa3FolderImportTask.NotPublished(state);

        Assert.Equal("ELS3-REUSABLE-DEPENDENCY-NOT-PUBLISHED", code);
        Assert.Equal("The reusable activity was not published (NotPublishedDependency, blocked by 'v1').", cause);
    }

    private static ActivityDiagnosticSubject Subject() => new("ActivityDefinitionVersion", "v1", "d1", "v1");

    private static Elsa3InputItem Input(string definitionId, string id, int version, string file) =>
        new(file, new Elsa3WorkflowDefinition { Id = id, DefinitionId = definitionId, Version = version });

    private static ReusableActivityImportPlan Plan(params ReusableActivityImportItem[] items) =>
        new("plan", "collection", items, []);

    private static ReusableActivityImportItem Item(string id, string? error = null, string? dependsOn = null) =>
        new(id, id, 1, "fingerprint", false, id, id, null, null, null,
            dependsOn is null ? [] : [new ReusableActivityDependency(id, dependsOn, dependsOn, dependsOn, "node")],
            [], [],
            error is null ? [] : [Error(error)]);

    private static Dictionary<string, Elsa3InputItem> Inputs(ReusableActivityImportPlan plan) =>
        plan.Items.ToDictionary(item => item.SourceVersionId, item => Input(item.SourceDefinitionId, item.SourceVersionId, 1, $"{item.SourceDefinitionId}.json"));

    private static Elsa3MigrationDiagnostic Error(
        string code,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<Elsa3MigrationPathSegment>? pathSegments = null) =>
        new(Elsa3MigrationDiagnosticSeverity.Error, code, $"{code} happened.", metadata: metadata, pathSegments: pathSegments);
}
