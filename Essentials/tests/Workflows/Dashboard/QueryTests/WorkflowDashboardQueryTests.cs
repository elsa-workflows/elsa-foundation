using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Elsa.Events;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management;
using Elsa.Workflows.Dashboard.Queries;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Dashboard.QueryTests;

public sealed class WorkflowDashboardQueryTests
{
    private static readonly DashboardRequestScope Caller = new("tenant-a", "user-a", "studio");
    private static readonly DateTimeOffset Current = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Baseline = Current.AddDays(-7);

    [Theory]
    [InlineData("default")]
    [InlineData("override")]
    [InlineData("missing")]
    public async Task Numeric_having_parameters_require_resolved_nonnull_values_before_resource_admission(string input)
    {
        using var host = new Host();
        var question = Aggregate();
        question = question with { Query = question.Query with {
            Parameters = [new("minimum", "decimal", Nullable: true,
                Default: input == "missing" ? null : input == "default" ? new("null") : new("decimal", "0"))],
            Having = new() { Metric = "current.started", Operator = "gt", Value = new("decimal", Parameter: "minimum") }
        }, ParameterValues = input == "override" ? new Dictionary<string, DashboardValue> { ["minimum"] = new("null") } : new Dictionary<string, DashboardValue>() };

        var validation = await host.Queries.ValidateAsync(Caller, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == (input == "missing" ? "INVALID_QUERY" : "INVALID_VALUE"));
        var admission = await Assert.ThrowsAsync<DashboardResourceException>(() =>
            host.Validator.ValidateAsync(new("tenant-a", "user-a", new HashSet<string>(), new HashSet<string>()), question).AsTask());
        Assert.Equal("validation", admission.Code);
        var execution = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, question).AsTask());
        Assert.Equal(input == "missing" ? "INVALID_QUERY" : "INVALID_VALUE", execution.Code);
        Assert.Empty(host.Store.Requests);
    }

    [Theory]
    [InlineData("isNull", 0)]
    [InlineData("isNotNull", 1)]
    public async Task Having_null_tests_remain_valid_without_a_numeric_operand(string operation, int expectedRows)
    {
        using var host = new Host();
        var question = Aggregate();
        question = question with { Query = question.Query with {
            Having = new() { Metric = "current.started", Operator = operation } } };
        Assert.True((await host.Queries.ValidateAsync(Caller, question)).IsValid);
        Assert.Equal(expectedRows, (await host.Queries.ExecuteAsync(Caller, question)).Rows.Count);
    }

    [Fact]
    public async Task Nullable_numeric_having_parameters_accept_nonnull_defaults_and_overrides()
    {
        using var host = new Host();
        await host.Add(Run("run", "definition", Current, WorkflowExecutionStatus.Completed));
        var question = Aggregate();
        question = question with { Query = question.Query with {
            Parameters = [new("minimum", "decimal", Nullable: true, Default: new("decimal", "0"))],
            Having = new() { Metric = "current.started", Operator = "gt", Value = new("decimal", Parameter: "minimum") } } };
        Assert.True((await host.Queries.ValidateAsync(Caller, question)).IsValid);
        Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        question = question with { ParameterValues = new Dictionary<string, DashboardValue> { ["minimum"] = new("integer", "2") } };
        Assert.True((await host.Queries.ValidateAsync(Caller, question)).IsValid);
        Assert.Empty((await host.Queries.ExecuteAsync(Caller, question)).Rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_current_metric_visualization_bindings_fail_before_retained_run_reads(bool acrossAxes)
    {
        using var host = new Host();
        await host.Add(Run("run", "definition", Current, WorkflowExecutionStatus.Completed));
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["definitionId"] },
            Visualization = new() { Kind = "bar", X = acrossAxes ? "started" : "definitionId",
                Y = acrossAxes ? ["current.started"] : ["started", "current.started"] } };

        var validation = await host.Queries.ValidateAsync(Caller, question);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Code == "UNSUPPORTED_QUERY" && problem.Path == "visualization");
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, question).AsTask());
        Assert.Equal("UNSUPPORTED_QUERY", failure.Code);
        Assert.Empty(host.Store.Requests);
    }

    [Theory]
    [InlineData("started", false)]
    [InlineData("current.started", false)]
    [InlineData("started", true)]
    [InlineData("current.started", true)]
    public async Task One_current_metric_spelling_returns_the_bound_native_columns_and_values(string binding, bool metricOnBothAxes)
    {
        using var host = new Host();
        await host.Add(Run("run-a", "definition", Current, WorkflowExecutionStatus.Completed));
        await host.Add(Run("run-b", "definition", Current, WorkflowExecutionStatus.Faulted));
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["definitionId"] },
            Visualization = new() { Kind = "bar", X = metricOnBothAxes ? binding : "definitionId", Y = [binding] } };

        Assert.True((await host.Queries.ValidateAsync(Caller, question)).IsValid);
        var result = await host.Queries.ExecuteAsync(Caller, question);
        var row = Assert.Single(result.Rows);
        Assert.Equal(2m, Number(row, binding));
        Assert.Contains(result.Columns, column => column.Id == binding);
        Assert.DoesNotContain(result.Columns, column => column.Id == (binding == "started" ? "current.started" : "started"));
        Assert.True(row.Values.ContainsKey(question.Visualization.X!));
        Assert.All(question.Visualization.Y, reference => Assert.True(row.Values.ContainsKey(reference)));
    }

    [Fact]
    public async Task Novel_comparison_filters_and_current_folder_groups_match_independent_counts()
    {
        using var host = new Host();
        await SeedComparison(host);
        var query = Comparison();
        var result = await host.Queries.ExecuteAsync(Caller, query);
        var row = Assert.Single(result.Rows);
        Assert.Equal("wf-a", row.Values["definitionId"].Value);
        Assert.Equal("/Operations", row.Values["currentFolder"].Value);
        Assert.Equal(50m, Number(row, "rate"));
        Assert.Equal(0m, Number(row, "baseline.rate"));
        Assert.Equal(50m, Number(row, "delta.rate"));
        Assert.Equal(2m, Number(row, "started"));
        Assert.Equal("unknown", result.Coverage.Status);
        Assert.Contains("current readable Design organization", result.Interpretation);
        Assert.All(host.Store.Requests, page => { Assert.Null(page.From); Assert.Null(page.To); Assert.Equal("tenant-a", page.TenantId); });
    }

    [Fact]
    public async Task Current_and_baseline_drill_recompute_authority_and_use_integrity_bound_pages()
    {
        using var host = new Host();
        await SeedComparison(host);
        var question = Comparison();
        var preview = await host.Queries.ExecuteAsync(Caller, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        var first = await host.Queries.DrillAsync(Caller, new(question, descriptor, 1));
        Assert.Single(first.Rows);
        Assert.NotNull(first.NextCursor);
        var next = await host.Queries.DrillAsync(Caller, new(question, descriptor, 1, first.NextCursor));
        Assert.Single(next.Rows);
        Assert.Null(next.NextCursor);
        Assert.NotEqual(first.Rows[0].Key, next.Rows[0].Key);
        Assert.Equal(new[] { "a-current-fail", "a-current-ok" }, first.Rows.Concat(next.Rows).Select(row => row.Key).Order(StringComparer.Ordinal));
        var baseline = await host.Queries.DrillAsync(Caller, new(question, descriptor with { Period = "baseline" }, 100));
        Assert.Equal(new[] { "a-prior-1", "a-prior-2" }, baseline.Rows.Select(row => row.Key));
        var wrongPeriodCursor = await Assert.ThrowsAsync<DashboardQueryException>(() =>
            host.Queries.DrillAsync(Caller, new(question, descriptor with { Period = "baseline" }, 1, first.NextCursor)).AsTask());
        Assert.Equal("INVALID_DRILL", wrongPeriodCursor.Code);
    }

    [Fact]
    public async Task Record_revocation_removes_group_membership_before_drill_or_having()
    {
        using var host = new Host();
        await SeedComparison(host);
        var question = Comparison();
        var preview = await host.Queries.ExecuteAsync(Caller, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        host.Access.DeniedRuns.Add("a-current-fail");
        Assert.Empty((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.DrillAsync(Caller, new(question, descriptor)).AsTask());
        Assert.Equal("INVALID_DRILL", failure.Code);
    }

    [Fact]
    public async Task Tampered_group_proof_is_rejected_without_executing_source_reads()
    {
        using var host = new Host();
        await SeedComparison(host);
        var question = Comparison();
        var preview = await host.Queries.ExecuteAsync(Caller, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        var reads = host.Store.Requests.Count;
        var tampered = descriptor with { GroupValues = new Dictionary<string, DashboardValue>
        {
            ["definitionId"] = new("string", "wf-b"), ["currentFolder"] = new("string", "/Operations")
        } };
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.DrillAsync(Caller, new(question, tampered)).AsTask());
        Assert.Equal("INVALID_DRILL", failure.Code);
        Assert.Equal(reads, host.Store.Requests.Count);
    }

    [Fact]
    public async Task Empty_cohort_has_no_denominator_while_a_successful_cohort_can_have_observed_zero()
    {
        using var host = new Host();
        var question = Aggregate([new("rate", "workflow.failure-rate", "started-current-status-v1"), new("started", "workflow.started-count", "v1")]);
        var empty = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Null(empty.Values["rate"].Value);
        Assert.Equal("noEligibleDenominator", empty.Values["rate"].NullReason);
        Assert.Equal(0m, Number(empty, "started"));
        await host.Add(Run("ok", "wf-a", Current, WorkflowExecutionStatus.Completed));
        var zero = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal(0m, Number(zero, "rate"));
        Assert.Null(zero.Values["rate"].NullReason);
    }

    [Fact]
    public async Task Duration_eligibility_and_new_field_aggregate_do_not_treat_missing_pairs_as_zero()
    {
        using var host = new Host();
        await host.Add(Run("thirty", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: 30));
        await host.Add(Run("ninety", "wf-a", Current, WorkflowExecutionStatus.Faulted, seconds: 90));
        await host.Add(Run("zero", "wf-a", Current, WorkflowExecutionStatus.Cancelled, seconds: 0));
        await host.Add(Run("running", "wf-a", Current, WorkflowExecutionStatus.Running, seconds: null));
        await host.Add(Run("missing", "wf-a", Current, WorkflowExecutionStatus.Faulted, seconds: null));
        await host.Add(Run("inverted", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: -10));
        var question = Aggregate([new("average", Aggregate: "average", Field: "durationMs"),
            new("eligible", "workflow.eligible-duration-count", "v1"), new("started", "workflow.started-count", "v1")]);
        var row = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal(40000m, Number(row, "average"));
        Assert.Equal(3m, Number(row, "eligible"));
        Assert.Equal(6m, Number(row, "started"));
    }

    [Theory]
    [InlineData("sum", 120000)]
    [InlineData("average", 60000)]
    [InlineData("min", 30000)]
    [InlineData("max", 90000)]
    [InlineData("count", 2)]
    public async Task Novel_duration_aggregates_use_the_exposed_field_contract(string aggregate, int expected)
    {
        using var host = new Host();
        await host.Add(Run("a", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: 30));
        await host.Add(Run("b", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: 90));
        await host.Add(Run("missing", "wf-a", Current, WorkflowExecutionStatus.Running, seconds: null));
        var result = await host.Queries.ExecuteAsync(Caller, Aggregate([new("value", Aggregate: aggregate, Field: "durationMs")]));
        Assert.Equal(expected, Number(Assert.Single(result.Rows), "value"));
        Assert.Equal(aggregate == "count" ? "runs" : "milliseconds", Assert.Single(result.Columns).Unit);
    }

    [Fact]
    public async Task Inspect_only_never_implies_the_source_aggregate_grant()
    {
        using var host = new Host();
        host.Permission.Granted = false;
        await host.Add(Run("visible-runtime-record", "wf-a", Current, WorkflowExecutionStatus.Completed));
        Assert.Empty(await host.Queries.DiscoverAsync(Caller));
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, Aggregate()).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", failure.Code);
        Assert.Empty(host.Store.Requests);
    }

    [Fact]
    public async Task Aggregate_grant_does_not_admit_records_without_individual_inspection_authority()
    {
        using var host = new Host();
        await host.Add(Run("hidden", "wf-a", Current, WorkflowExecutionStatus.Faulted));
        host.Access.DeniedRuns.Add("hidden");
        var row = Assert.Single((await host.Queries.ExecuteAsync(Caller, Aggregate())).Rows);
        Assert.Equal(0m, Number(row, "started"));
    }

    [Fact]
    public async Task Canonical_actor_and_inspection_subject_must_match_the_actual_request()
    {
        using var host = new Host();
        Assert.Empty(await host.Queries.DiscoverAsync(Caller with { TenantId = "tenant-b" }));
        Assert.Empty(await host.Queries.DiscoverAsync(Caller with { SubjectId = "user-b" }));
        host.Access.Subject = "another-external-subject";
        Assert.Empty(await host.Queries.DiscoverAsync(Caller));
        Assert.Empty(host.Store.Requests);
    }

    [Fact]
    public async Task Source_rechecks_aggregate_permission_between_validation_and_execution()
    {
        using var host = new Host();
        host.Permission.MaximumGrantedCalls = 2;
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, Aggregate()).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", failure.Code);
        Assert.Empty(host.Store.Requests);
    }

    [Fact]
    public async Task Current_folder_enrichment_intersects_design_and_runtime_authority_before_aggregation()
    {
        using var host = new Host();
        await host.Add(Run("a", "wf-a", Current, WorkflowExecutionStatus.Faulted));
        await host.Add(Run("b", "wf-b", Current, WorkflowExecutionStatus.Completed));
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-operations", "Operations");
        host.Library.Organizations["wf-b"] = Organization("wf-b", "folder-finance", "Finance");
        host.Access.DeniedDefinitions.Add("wf-a");
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["currentFolder"] } };
        var row = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal("/Finance", row.Values["currentFolder"].Value);
        Assert.Equal(1m, Number(row, "started"));
        Assert.DoesNotContain("Operations", string.Join(" ", row.Values.Values.Select(value => value.Value)));
    }

    [Fact]
    public async Task Folder_movement_changes_current_attribution_without_claiming_historical_membership()
    {
        using var host = new Host();
        await host.Add(Run("a", "wf-a", Current, WorkflowExecutionStatus.Completed));
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-old", "Old");
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["currentFolder"] } };
        var before = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal("/Old", before.Values["currentFolder"].Value);
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-new", "New");
        var after = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal("/New", after.Values["currentFolder"].Value);
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.DrillAsync(Caller, new(question, before.Drill!)).AsTask());
        Assert.Equal("INVALID_DRILL", failure.Code);
    }

    [Fact]
    public async Task Runtime_only_composition_does_not_advertise_or_substitute_folder_fields()
    {
        using var host = new Host(includeDesign: false);
        var dataset = Assert.Single(await host.Queries.DiscoverAsync(Caller));
        Assert.DoesNotContain(dataset.Fields, field => field.Id == "currentFolder");
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["currentFolder"] } };
        Assert.False((await host.Queries.ValidateAsync(Caller, question)).IsValid);
    }

    [Fact]
    public async Task Traversal_bound_refuses_a_result_instead_of_returning_a_sample()
    {
        using var host = new Host(limits: new() { MaximumSourceRows = 3 });
        for (var i = 0; i < 4; i++) await host.Add(Run("run-" + i, "wf-a", Current, WorkflowExecutionStatus.Completed));
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, Aggregate()).AsTask());
        Assert.Equal("QUERY_LIMIT", failure.Code);
    }

    [Fact]
    public async Task Group_bound_is_checked_before_output_limit()
    {
        using var host = new Host(limits: new() { MaximumGroups = 1 });
        await host.Add(Run("a", "wf-a", Current, WorkflowExecutionStatus.Completed));
        await host.Add(Run("b", "wf-b", Current, WorkflowExecutionStatus.Completed));
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["definitionId"], Limit = 1 } };
        var failure = await Assert.ThrowsAsync<DashboardQueryException>(() => host.Queries.ExecuteAsync(Caller, question).AsTask());
        Assert.Equal("QUERY_LIMIT", failure.Code);
    }

    [Fact]
    public async Task Excluding_tests_preserves_unknown_and_requires_explicit_published_only_selection()
    {
        using var host = new Host();
        await host.Add(Run("published", "wf-a", Current, WorkflowExecutionStatus.Completed));
        await host.Add(Run("legacy", "wf-a", Current, WorkflowExecutionStatus.Completed) with { RunKind = WorkflowRunKind.Unknown });
        await host.Add(Run("test", "wf-a", Current, WorkflowExecutionStatus.Faulted) with { RunKind = WorkflowRunKind.TestRun });
        var question = Aggregate();
        question = question with { Query = question.Query with { Filters = new() { Field = "runKind", Operator = "ne", Value = new("string", "testRun") } } };
        Assert.Equal(2m, Number(Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows), "started"));
        question = question with { Query = question.Query with { Filters = new() { Field = "runKind", Operator = "eq", Value = new("string", "publishedRun") } } };
        Assert.Equal(1m, Number(Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows), "started"));
    }

    [Fact]
    public async Task Typed_parameters_compound_filters_and_ordinary_record_sorting_use_the_same_source()
    {
        using var host = new Host();
        await host.Add(Run("thirty", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: 30));
        await host.Add(Run("ninety", "wf-a", Current, WorkflowExecutionStatus.Completed, seconds: 90));
        var question = Aggregate();
        question = question with { Query = question.Query with
        {
            Parameters = [new("minimumMs", "decimal", true, Default: new("decimal", "40000"))],
            Filters = new() { All = [new() { Field = "durationMs", Operator = "gte", Value = new("decimal", Parameter: "minimumMs") },
                new() { Field = "status", Operator = "eq", Value = new("string", "completed") }] },
            Form = "records", Metrics = [], Fields = ["executionId", "durationMs"], Sort = [new("durationMs", "descending")]
        } };
        var row = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal("ninety", row.Values["executionId"].Value);
        Assert.Equal(90000m, Number(row, "durationMs"));
    }

    [Fact]
    public async Task Source_does_not_invent_business_dimensions_from_runtime_metadata()
    {
        using var host = new Host();
        var state = Run("a", "wf-a", Current, WorkflowExecutionStatus.Completed) with
        { SystemMetadata = new Dictionary<string, string> { ["customerCategory"] = "protected-gold" } };
        await host.Add(state);
        var dataset = Assert.Single(await host.Queries.DiscoverAsync(Caller));
        Assert.DoesNotContain(dataset.Fields, field => field.BusinessDimension || field.Id.Contains("customer", StringComparison.OrdinalIgnoreCase));
        var question = Aggregate();
        question = question with { Query = question.Query with { GroupBy = ["customerCategory"] } };
        Assert.False((await host.Queries.ValidateAsync(Caller, question)).IsValid);
    }

    [Fact]
    public async Task A_folder_rate_uses_all_eligible_runs_rather_than_an_average_of_workflow_rates()
    {
        using var host = new Host();
        await host.Add(Run("one-failure", "wf-a", Current, WorkflowExecutionStatus.Faulted));
        for (var i = 0; i < 9; i++) await host.Add(Run("ok-" + i, "wf-b", Current, WorkflowExecutionStatus.Completed));
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-ops", "Operations");
        host.Library.Organizations["wf-b"] = Organization("wf-b", "folder-ops", "Operations");
        var question = Aggregate([new("rate", "workflow.failure-rate", "started-current-status-v1")]);
        question = question with { Query = question.Query with { GroupBy = ["currentFolder"] } };
        Assert.Equal(10m, Number(Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows), "rate"));
    }

    [Fact]
    public async Task Equal_failure_ratios_with_different_denominators_are_not_reported_as_an_increase()
    {
        using var host = new Host();
        for (var i = 0; i < 6; i++) await host.Add(Run("now-" + i, "wf-a", Current, i < 2 ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed));
        for (var i = 0; i < 3; i++) await host.Add(Run("prior-" + i, "wf-a", Baseline, i < 1 ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed));
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-ops", "Operations");
        Assert.Empty((await host.Queries.ExecuteAsync(Caller, Comparison())).Rows);
    }

    [Fact]
    public async Task Long_retained_durations_can_be_summed_without_overflowing_display_intermediates()
    {
        using var host = new Host();
        var started = DateTimeOffset.MinValue;
        for (var i = 0; i < 1500; i++) await host.Add(Run("old-" + i, "wf-a", started, WorkflowExecutionStatus.Completed, seconds: null) with { CompletedAt = Current });
        var question = Aggregate([new("sum", Aggregate: "sum", Field: "durationMs")]);
        question = question with { Query = question.Query with { Time = new() { Field = "startedAt", From = started, To = started.AddDays(1) } } };
        var row = Assert.Single((await host.Queries.ExecuteAsync(Caller, question)).Rows);
        Assert.Equal((Current - started).Ticks / 10000m * 1500, Number(row, "sum"));
    }

    [Fact]
    public async Task Canonical_resource_policy_can_deny_a_field_or_metric_question_before_source_reads()
    {
        using var host = new Host();
        host.Permission.Predicate = context => context.Resource is not DashboardQueryDefinition query ||
            !query.Metrics.Any(metric => metric.Field == "durationMs" || metric.Metric == "workflow.average-duration-ms");
        var validation = await host.Queries.ValidateAsync(Caller, Aggregate([new("average", Aggregate: "average", Field: "durationMs")]));
        Assert.False(validation.IsValid);
        Assert.Empty(host.Store.Requests);
    }

    [Theory]
    [InlineData("completedAt")]
    [InlineData("durationMs")]
    public async Task Aggregate_permission_never_implies_permission_for_drill_record_fields(string forbiddenField)
    {
        using var host = new Host();
        await host.Add(Run("current", "wf-a", Current, WorkflowExecutionStatus.Completed));
        var question = Aggregate();
        var preview = await host.Queries.ExecuteAsync(Caller, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        var before = host.Store.Requests.Count;
        host.Permission.Predicate = context => context.Resource is not DashboardQueryDefinition query ||
            query.Form != "records" || !query.Fields.Contains(forbiddenField, StringComparer.Ordinal);
        var denied = await Assert.ThrowsAsync<DashboardQueryException>(() =>
            host.Queries.DrillAsync(Caller, new(question, descriptor, 100)).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", denied.Code);
        Assert.Equal(before, host.Store.Requests.Count);
        host.Permission.Predicate = _ => true;
        Assert.Equal("current", Assert.Single((await host.Queries.DrillAsync(Caller, new(question, descriptor, 100))).Rows).Key);
    }

    [Fact]
    public async Task Baseline_drill_authorizes_its_absolute_record_window_not_the_current_period()
    {
        using var host = new Host();
        await SeedComparison(host);
        var question = Comparison();
        var preview = await host.Queries.ExecuteAsync(Caller, question);
        var descriptor = Assert.Single(preview.Rows).Drill!;
        host.Permission.Predicate = context => context.Resource is not DashboardQueryDefinition query ||
            query.Form != "records" || query.Time?.From >= new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var current = await host.Queries.DrillAsync(Caller, new(question, descriptor, 100));
        Assert.Equal(2, current.Rows.Count);
        var before = host.Store.Requests.Count;
        var denied = await Assert.ThrowsAsync<DashboardQueryException>(() =>
            host.Queries.DrillAsync(Caller, new(question, descriptor with { Period = "baseline" }, 100)).AsTask());
        Assert.Equal("DATASET_UNAVAILABLE", denied.Code);
        Assert.Equal(before, host.Store.Requests.Count);
    }

    private static decimal Number(DashboardResultRow row, string key) => decimal.Parse(row.Values[key].Value!, CultureInfo.InvariantCulture);

    private static DashboardQueryRequest Aggregate(IReadOnlyList<DashboardMetricSelection>? metrics = null) => new()
    {
        Query = new()
        {
            Dataset = new("elsa.workflows.runs", "runs-v1"),
            Metrics = metrics ?? [new("started", "workflow.started-count", "v1")],
            Time = new() { Field = "startedAt", From = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), To = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero) }
        }
    };

    private static DashboardQueryRequest Comparison()
    {
        var question = Aggregate([new("rate", "workflow.failure-rate", "started-current-status-v1"), new("started", "workflow.started-count", "v1")]);
        return question with { Query = question.Query with
        {
            GroupBy = ["definitionId", "currentFolder"],
            Comparison = new("absolute", new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)),
            Filters = new() { Field = "runKind", Operator = "ne", Value = new("string", "testRun") },
            Having = new() { Metric = "delta.rate", Operator = "gt", Value = new("decimal", "0") },
            Sort = [new("delta.rate", "descending")]
        } };
    }

    private static WorkflowExecutionState Run(string id, string definition, DateTimeOffset started, WorkflowExecutionStatus status,
        int? seconds = 30, string tenant = "tenant-a") => new(id, new("artifact-" + definition, definition, "version-1", "1.0", "hash"), status, null,
        started, started, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), seconds is { } duration ? started.AddSeconds(duration) : null,
        null, null, tenant, new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun };

    private static WorkflowOrganization Organization(string definition, string folderId, string name) => new(definition, folderId,
        [new(folderId, null, name, name.ToUpperInvariant(), Current, Current, "r1")], [], "r1");

    private static async Task SeedComparison(Host host)
    {
        foreach (var state in new[]
        {
            Run("a-current-fail", "wf-a", Current, WorkflowExecutionStatus.Faulted), Run("a-current-ok", "wf-a", Current, WorkflowExecutionStatus.Completed),
            Run("a-prior-1", "wf-a", Baseline, WorkflowExecutionStatus.Completed), Run("a-prior-2", "wf-a", Baseline, WorkflowExecutionStatus.Completed),
            Run("b-current-1", "wf-b", Current, WorkflowExecutionStatus.Completed), Run("b-current-2", "wf-b", Current, WorkflowExecutionStatus.Completed),
            Run("b-prior-fail", "wf-b", Baseline, WorkflowExecutionStatus.Faulted), Run("b-prior-ok", "wf-b", Baseline, WorkflowExecutionStatus.Completed),
            Run("excluded-test", "wf-a", Current, WorkflowExecutionStatus.Faulted) with { RunKind = WorkflowRunKind.TestRun },
            Run("denied-fail", "wf-a", Current, WorkflowExecutionStatus.Faulted), Run("other-tenant", "wf-a", Current, WorkflowExecutionStatus.Faulted, tenant: "tenant-b")
        }) await host.Add(state);
        host.Access.DeniedRuns.Add("denied-fail");
        host.Library.Organizations["wf-a"] = Organization("wf-a", "folder-operations", "Operations");
        host.Library.Organizations["wf-b"] = Organization("wf-b", "folder-operations", "Operations");
    }

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        public TrackingStore Store { get; } = new();
        public Access Access { get; } = new();
        public PermissionService Permission { get; } = new();
        public WorkflowLibraryProxy Library { get; }
        public IDashboardQueryService Queries { get; }
        public IDashboardDefinitionValidator Validator { get; }
        public Host(bool includeDesign = true, DashboardQueryLimits? limits = null)
        {
            var library = DispatchProxy.Create<IWorkflowLibraryStore, WorkflowLibraryProxy>();
            Library = (WorkflowLibraryProxy)library;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
            if (limits is not null) services.AddSingleton(limits);
            services.AddSingleton<IWorkflowExecutionStateStore>(Store);
            services.AddSingleton<IActivityInspectionContextAsync>(Access);
            services.AddSingleton<IDashboardActorContext>(new ActorContext());
            services.AddSingleton<IPermissionAuthorizationService>(Permission);
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "external-a")], "test")) } });
            if (includeDesign)
            {
                services.AddSingleton<IWorkflowDefinitionAuthorizer>(Access);
                services.AddSingleton(library);
            }
            new EventsFeature().ConfigureServices(services);
            new DashboardManagementFeature().ConfigureServices(services);
            new WorkflowDashboardQueriesFeature().ConfigureServices(services);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scope = _provider.CreateScope();
            Queries = _scope.ServiceProvider.GetRequiredService<IDashboardQueryService>();
            Validator = _scope.ServiceProvider.GetRequiredService<IDashboardDefinitionValidator>();
        }
        public ValueTask<WorkflowExecutionState> Add(WorkflowExecutionState state) => Store.SaveAsync(state);
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }

    private sealed class ActorContext : IDashboardActorContext
    {
        public ValueTask<DashboardActor> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new DashboardActor("tenant-a", "user-a",
            new HashSet<string>(), new HashSet<string>()) { AuthenticationSubjectId = "external-a" });
    }

    private sealed class PermissionService : IPermissionAuthorizationService
    {
        public bool Granted { get; set; } = true;
        public int MaximumGrantedCalls { get; set; } = int.MaxValue;
        public Func<PermissionEvaluationContext, bool> Predicate { get; set; } = _ => true;
        private int _calls;
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Granted && ++_calls <= MaximumGrantedCalls && Predicate(context) && context.Permission == WorkflowDashboardQueryPermissions.Read
                ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied());
        }
    }

    private sealed class Access : IActivityInspectionContextAsync, IWorkflowDefinitionAuthorizer
    {
        public HashSet<string> DeniedRuns { get; } = [];
        public HashSet<string> DeniedDefinitions { get; } = [];
        public string Subject { get; set; } = "external-a";
        public string TenantScope => "tenant:tenant-a";
        public string AuditSubject => Subject;
        public string RequestCorrelationId => "test";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken) => ValueTask.FromResult("test-profile");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState state, CancellationToken cancellationToken) => ValueTask.FromResult(state.TenantId == "tenant-a" && !DeniedRuns.Contains(state.WorkflowExecutionId));
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState state, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState state, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken) => ValueTask.FromResult(!DeniedDefinitions.Contains(definitionId));
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class TrackingStore : IWorkflowExecutionStateStore
    {
        private readonly InMemoryWorkflowExecutionStateStore _store = new();
        public List<WorkflowExecutionStatePageQuery> Requests { get; } = [];
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => _store.SaveAsync(state, cancellationToken);
        public ValueTask<WorkflowExecutionState?> FindAsync(string id, CancellationToken cancellationToken = default) => _store.FindAsync(id, cancellationToken);
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("The query must use finite normal history pages.");
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default)
        { Requests.Add(query); return _store.QueryPageAsync(query, cancellationToken); }
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => _store.ListPinnedExecutableArtifactIdsAsync(cancellationToken);
        public ValueTask<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => _store.DeleteAsync(id, cancellationToken);
    }
}

public class WorkflowLibraryProxy : DispatchProxy
{
    public Dictionary<string, WorkflowOrganization> Organizations { get; } = new(StringComparer.Ordinal);
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IWorkflowLibraryStore.GetOrganizationAsync))
            return Task.FromResult(Organizations.GetValueOrDefault((string)args![0]!));
        throw new InvalidOperationException("This witness permits only the owning organization read port.");
    }
}
