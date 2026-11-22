using Elsa.Modularity.Planning.Models;
using Elsa.Modularity.Planning.Services;

namespace Elsa.Modularity.Planning.Tests;

public sealed class SelectionExpansionTests
{
    private static readonly string[] s_embedded =
    [
        "ActivitiesControlFlow", "ActivitiesPrimitives", "ActivitiesRuntime", "ActivitiesSequence", "Events", "Expressions", "Mediator",
        "Primitives", "Serialization", "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption", "WorkflowsRuntimeTriggers"
    ];

    private static readonly string[] s_authoring =
    [
        "ActivitiesDesignApi", "ActivitiesDesignEntityFrameworkCore", "ActivitiesDesignReconciliation", "ApiCapabilities",
        "ClrActivityReconciliation", "Events", "Expressions", "Mediator", "Primitives", "Serialization", "WorkflowDesignValidations",
        "WorkflowsDesignApi", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishing", "WorkflowsPublishingApi", "WorkflowsPublishingEntityFrameworkCore"
    ];

    private static readonly string[] s_worker =
    [
        "ActivitiesControlFlow", "ActivitiesPrimitives", "ActivitiesRuntime", "ActivitiesSequence", "ApiCapabilities", "Events", "Expressions",
        "Mediator", "Primitives", "Serialization", "WorkflowsRuntimeApi", "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];

    private static readonly string[] s_custom =
    [
        "ActivitiesControlFlow", "ActivitiesPrimitives", "ActivitiesRuntime", "ActivitiesSequence", "ApiCapabilities", "DiagnosticsOpenTelemetry",
        "DiagnosticsOpenTelemetryEntityFrameworkCore", "DiagnosticsStructuredLogs", "DiagnosticsStructuredLogsEntityFrameworkCore", "Events",
        "Expressions", "Mediator", "Primitives", "Serialization", "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];

    [Fact]
    public void Four_planning_fixtures_expand_to_exact_documented_sets()
    {
        var foundation = PlannerFixture.Definition("group", "foundation-core", ["Primitives", "Serialization", "Mediator", "Events", "Expressions"]);
        var runtime = PlannerFixture.Definition("group", "runtime-base", ["ActivitiesRuntime", "ActivitiesPrimitives", "ActivitiesControlFlow", "ActivitiesSequence", "WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeResumption", "WorkflowsRuntimeTriggers"]);
        var authoring = PlannerFixture.Definition("group", "authoring-base", ["ApiCapabilities", "ActivitiesDesignApi", "ActivitiesDesignEntityFrameworkCore", "ActivitiesDesignReconciliation", "ClrActivityReconciliation", "WorkflowDesignValidations", "WorkflowsDesignApi", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishing", "WorkflowsPublishingApi", "WorkflowsPublishingEntityFrameworkCore"]);
        var workerHttp = PlannerFixture.Definition("group", "worker-http", ["ApiCapabilities", "WorkflowsRuntimeApi"]);
        var diagnostics = PlannerFixture.Definition("group", "diagnostics-ef", ["DiagnosticsOpenTelemetry", "DiagnosticsOpenTelemetryEntityFrameworkCore", "DiagnosticsStructuredLogs", "DiagnosticsStructuredLogsEntityFrameworkCore"]);
        var catalog = PlannerFixture.Catalog(groups: [foundation, runtime, authoring, workerHttp, diagnostics]);

        AssertExact(catalog, [foundation, runtime], s_embedded);
        AssertExact(catalog, [foundation, authoring], s_authoring);
        AssertExact(catalog, [foundation, runtime, workerHttp], s_worker);

        var customDefinition = PlannerFixture.Definition("profile", "custom-worker", s_worker);
        var custom = SelectionPlanner.Plan(catalog,
            PlannerFixture.Authored(catalog, PlannerFixture.Ref(customDefinition, "workspace"), [PlannerFixture.Ref(diagnostics)], remove: ["WorkflowsRuntimeApi"], accepted: s_custom),
            workspaceProfiles: [new WorkspaceProfile("1", customDefinition)]);
        Assert.Equal(s_custom, custom.SelectedFeatureIds.ToArray());
        Assert.Equal(17, custom.SelectedFeatureIds.Length);
        Assert.Contains(custom.Reasons, reason => reason.FeatureId == "WorkflowsRuntimeApi" && reason.Action == "removed");
    }

    [Fact]
    public void Profile_group_overlap_addition_and_removal_keep_every_reason()
    {
        var profile = PlannerFixture.Definition("profile", "p", ["A", "B"]);
        var group = PlannerFixture.Definition("group", "g", ["B", "C"]);
        var catalog = PlannerFixture.Catalog(profiles: [profile], groups: [group]);
        var authored = PlannerFixture.Authored(catalog, PlannerFixture.Ref(profile), [PlannerFixture.Ref(group)], add: ["D"], remove: ["B"], accepted: ["A", "C", "D"]);

        var plan = SelectionPlanner.Plan(catalog, authored);
        Assert.Equal(new[] { "A", "C", "D" }, plan.SelectedFeatureIds.ToArray());
        Assert.Equal(new[] { "explicit-remove", "group", "profile" }, plan.Reasons.Where(reason => reason.FeatureId == "B").Select(reason => reason.SourceKind).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(plan.Findings, finding => finding.Code == "candidate-re-resolution");
    }

    [Fact]
    public void Declaration_order_does_not_change_ids_reasons_or_findings()
    {
        var first = PlannerFixture.Definition("group", "first", ["B", "A"]);
        var second = PlannerFixture.Definition("group", "second", ["C", "B"]);
        var catalogOne = PlannerFixture.Catalog(groups: [first, second]);
        var catalogTwo = PlannerFixture.Catalog(groups: [second, first]);
        var left = SelectionPlanner.Plan(catalogOne, PlannerFixture.Authored(catalogOne, groups: [PlannerFixture.Ref(first), PlannerFixture.Ref(second)], accepted: ["A", "B", "C"]));
        var right = SelectionPlanner.Plan(catalogTwo, PlannerFixture.Authored(catalogTwo, groups: [PlannerFixture.Ref(second), PlannerFixture.Ref(first)], accepted: ["A", "B", "C"]));

        Assert.Equal(catalogOne.Digest, catalogTwo.Digest);
        Assert.Equal(left.SelectedFeatureIds.ToArray(), right.SelectedFeatureIds.ToArray());
        Assert.Equal(left.Reasons.ToArray(), right.Reasons.ToArray());
        Assert.Equal(left.Findings.ToArray(), right.Findings.ToArray());
    }

    [Fact]
    public void No_profile_and_no_groups_is_a_valid_empty_selection()
    {
        var catalog = PlannerFixture.Catalog();
        var plan = SelectionPlanner.Plan(catalog, PlannerFixture.Authored(catalog));
        Assert.Empty(plan.SelectedFeatureIds);
        Assert.Empty(plan.Reasons);
    }

    private static void AssertExact(SelectionCatalog catalog, SelectionDefinition[] groups, string[] expected)
    {
        var plan = SelectionPlanner.Plan(catalog,
            PlannerFixture.Authored(catalog, groups: groups.Select(group => PlannerFixture.Ref(group)), accepted: expected));
        Assert.Equal(expected, plan.SelectedFeatureIds.ToArray());
        Assert.DoesNotContain(plan.Findings, finding => finding.Code == "candidate-re-resolution");
    }
}
