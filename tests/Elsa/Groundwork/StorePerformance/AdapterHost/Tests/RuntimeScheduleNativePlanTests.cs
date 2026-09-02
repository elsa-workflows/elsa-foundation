using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RuntimeScheduleNativePlanTests
{
    [Theory]
    [InlineData("due-timer-selection", "list-due", "runtime_durable_timer", "by_due_time_and_timer_id", 2048, 50)]
    [InlineData("recurring-schedule-selection", "list-due", "runtime_recurring_trigger_schedule", "by_active_next_occurrence_and_schedule_id", 2048, 50)]
    [InlineData("recurring-schedule-selection", "page-by-publication", "runtime_recurring_trigger_schedule", "by_activation_and_schedule_id", 2048, 50)]
    public void Route_definitions_bind_the_frozen_bounded_contract(
        string workloadId,
        string route,
        string table,
        string index,
        int physicalCardinality,
        int finiteLimit)
    {
        var definition = RuntimeScheduleNativePlan.Definition(workloadId, route);

        Assert.Equal(table, definition.TableName);
        Assert.Equal(index, definition.IndexName);
        Assert.Equal(physicalCardinality, definition.PhysicalCardinality);
        Assert.Equal(finiteLimit, definition.FiniteLimit);
        Assert.Equal(finiteLimit, definition.MaterializedCandidateCount);
        Assert.NotEmpty(definition.PredicateFields);
        Assert.NotEmpty(definition.OrderFields);
    }

    [Fact]
    public void SQLite_plan_rejects_scan_sort_and_materialization()
    {
        var definition = RuntimeScheduleNativePlan.Definition(
            RuntimeDueTimerSelectionWorkload.WorkloadId,
            "list-due");
        var command = "SELECT * FROM runtime_durable_timer WHERE __groundwork_scope = @scope AND timerDueTime <= @due ORDER BY timerDueTime ASC, timerId ASC LIMIT @limit";
        var physicalIndex = RuntimeScheduleNativePlan.ExpectedPhysicalIndexName("sqlite", definition);
        var plan = $"SEARCH runtime_durable_timer USING COVERING INDEX {physicalIndex} (timerDueTime<?)";
        var evidence = Evidence(definition, command, plan, physicalIndex);

        RuntimeScheduleNativePlan.Validate("sqlite", evidence, RuntimeScheduleNativePlan.Create(
            "sqlite", definition.WorkloadId, definition.RouteIdentity, command,
            plan));

        foreach (var unsafePlan in new[]
                 {
                     "SCAN runtime_durable_timer",
                     "SEARCH runtime_durable_timer USING INDEX by_due_time_and_timer_id; USE TEMP B-TREE FOR ORDER BY",
                     $"SEARCH runtime_durable_timer USING INDEX {physicalIndex}; MATERIALIZE page"
                 })
        {
            Assert.Throws<PerformanceContractException>(() => RuntimeScheduleNativePlan.Validate(
                "sqlite",
                evidence,
                RuntimeScheduleNativePlan.Create("sqlite", definition.WorkloadId, definition.RouteIdentity, command, unsafePlan)));
        }
    }

    [Fact]
    public void MongoDB_due_route_requires_a_lte_due_filter_and_exact_ixscan()
    {
        var definition = RuntimeScheduleNativePlan.Definition(
            RuntimeDueTimerSelectionWorkload.WorkloadId,
            "list-due");
        var command = "{\"find\":\"runtime_durable_timer\",\"filter\":{\"__groundwork_scope\":{\"$eq\":\"scope\"},\"timerDueTime\":{\"$lte\":\"asOf\"}},\"sort\":{\"timerDueTime\":1,\"timerId\":1},\"limit\":50}";
        var plan = "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"by_due_time_and_timer_id\"}}}";
        var evidence = Evidence(definition, command, plan, definition.IndexName);

        RuntimeScheduleNativePlan.Validate(
            "mongodb",
            evidence,
            RuntimeScheduleNativePlan.Create("mongodb", definition.WorkloadId, definition.RouteIdentity, command, plan));

        foreach (var invalidCommand in new[]
                 {
                     command.Replace("$lte", "$eq", StringComparison.Ordinal),
                     command.Replace("__groundwork_scope", "unexpected", StringComparison.Ordinal),
                     command.Replace("$lte", "$gte", StringComparison.Ordinal)
                 })
        {
            Assert.Throws<PerformanceContractException>(() => RuntimeScheduleNativePlan.Validate(
                "mongodb",
                evidence,
                RuntimeScheduleNativePlan.Create("mongodb", definition.WorkloadId, definition.RouteIdentity, invalidCommand, plan)));
        }
    }

    private static NativeRouteEvidence Evidence(
        RuntimeScheduleNativePlan.RouteDefinition definition,
        string command,
        string plan,
        string? physicalIndexName = null) =>
        new(
            definition.RouteIdentity,
            $"{definition.RouteIdentity}.raw.txt",
            new string('a', 64),
            "index-search",
            physicalIndexName ?? definition.IndexName,
            definition.PhysicalCardinality,
            HasStorageScopePredicate: true,
            HasRoutePredicate: true,
            definition.FiniteLimit,
            definition.MaterializedCandidateCount);
}
