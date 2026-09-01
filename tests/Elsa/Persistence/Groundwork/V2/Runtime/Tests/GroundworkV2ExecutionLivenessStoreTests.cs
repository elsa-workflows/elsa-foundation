using System.Security.Cryptography;
using System.Text;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using QueryPredicate = Groundwork.Query.Model.Predicate;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2ExecutionLivenessStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_round_trips_scoped_rows_pages_and_compare_and_swap()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        await AssertStoreBehaviorAsync(runtime);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_liveness_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(string.IsNullOrWhiteSpace(connectionString), $"Set {EnvironmentVariable(providerName)} to run the {providerName} liveness gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        await AssertStoreBehaviorAsync(runtime);
    }

    [Fact]
    public async Task Sqlite_recovery_routes_are_exactly_bounded_and_provider_ordered()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var request = new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10);

        Assert.Empty(await scanner.ScanAsync(request));
        AssertUnfilteredRecoveryRoutes(source, request.Limit);

        source.QueryRequests.Clear();
        Assert.Empty(await scanner.ScanAsync(new RuntimeRecoveryScanRequest(request.Now, request.LeaseTimeout, request.HeartbeatTimeout, request.Limit, "worker-a")));
        AssertOwnerRecoveryRoutes(source, request.Limit);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_pages_correlated_candidates_and_is_stable_after_reopen()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var units = UniqueRuntimeUnits();

        IReadOnlyList<string> secondPageIds;
        string continuation;
        using (var connection = runtime.OpenConnection())
        {
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);

            var source = new DirectSessionSource(connection, units);
            var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var codec = TestCodec();
            IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
            IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);
            ISchedulerStateStore schedulers = new GroundworkV2SchedulerStateStore(source, scope);
            IIncidentStateStore incidents = new GroundworkV2IncidentStateStore(source, scope);
            IWorkflowHoldStateStore holds = new GroundworkV2WorkflowHoldStateStore(source, scope);

            for (var index = 1; index <= 3; index++)
            {
                var workflowId = $"wf-page-{index}";
                await liveness.SaveAsync(State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-index)));
                await executions.SaveAsync(WorkflowState(workflowId));
                await schedulers.SaveAsync(new SchedulerState(workflowId, index));
                await incidents.SaveAsync(new IncidentState(
                    $"incident-{index}",
                    workflowId,
                    null,
                    null,
                    IncidentSeverity.Warning,
                    IncidentStatus.Open,
                    null,
                    "failure",
                    "recovery page test incident",
                    Now.AddMinutes(-index),
                    null));
            }

            await liveness.SaveAsync(State("wf-page-terminal", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(10)));
            await executions.SaveAsync(WorkflowState("wf-page-terminal", WorkflowExecutionStatus.Completed));
            await liveness.SaveAsync(State("wf-page-held", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(9)));
            await executions.SaveAsync(WorkflowState("wf-page-held"));
            await holds.SaveAsync(new WorkflowHoldState(
                "hold-page",
                "wf-page-held",
                activeHolds: [WorkflowHold.ForWorkflowExecution(
                    "hold-1",
                    "wf-page-held",
                    Now.AddMinutes(-9),
                    "recovery-test",
                    "held for recovery test")]));

            var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: codec);
            var first = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a"));
            Assert.Equal(["wf-page-3"], first.Items.Select(candidate => candidate.WorkflowExecutionId));
            Assert.All(first.Items, candidate => Assert.Equal("Running", candidate.Metadata["runtime.recovery.correlation.execution"]));
            Assert.All(first.Items, candidate => Assert.Equal("true", candidate.Metadata["runtime.recovery.correlation.incident"]));
            Assert.All(first.Items, candidate => Assert.Equal("true", candidate.Metadata["runtime.recovery.correlation.scheduler"]));
            Assert.NotNull(first.NextContinuationToken);
            var tokenParts = first.NextContinuationToken!.Split('.');
            var tamperedChecksum = (tokenParts[2][0] == 'A' ? 'B' : 'A') + tokenParts[2][1..];
            var tampered = $"{tokenParts[0]}.{tokenParts[1]}.{tamperedChecksum}";
            await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                tampered)).AsTask());
            var wrongKeyScanner = new GroundworkV2RuntimeRecoveryScanner(
                source,
                scope,
                continuationCodec: TestCodec("different-recovery-continuation-key-32-bytes"));
            await Assert.ThrowsAsync<ArgumentException>(() => wrongKeyScanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                first.NextContinuationToken)).AsTask());
            var otherScopeScanner = new GroundworkV2RuntimeRecoveryScanner(
                source,
                new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))),
                continuationCodec: TestCodec());
            await Assert.ThrowsAsync<ArgumentException>(() => otherScopeScanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                first.NextContinuationToken)).AsTask());
            continuation = first.NextContinuationToken!;

            var second = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                continuation));
            Assert.Equal(["wf-page-2"], second.Items.Select(candidate => candidate.WorkflowExecutionId));
            Assert.NotNull(second.NextContinuationToken);
            secondPageIds = second.Items.Select(candidate => candidate.WorkflowExecutionId).ToArray();
            var third = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                second.NextContinuationToken));
            Assert.Equal(["wf-page-1"], third.Items.Select(candidate => candidate.WorkflowExecutionId));
        }

        using (var reopenedConnection = runtime.OpenConnection())
        {
            var source = new DirectSessionSource(reopenedConnection, units);
            var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var scanner = new GroundworkV2RuntimeRecoveryScanner(
                source,
                scope,
                continuationCodec: TestCodec());
            var reopened = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                2,
                "worker-a",
                continuation));
            Assert.Equal(secondPageIds, reopened.Items.Select(candidate => candidate.WorkflowExecutionId));
        }
    }

    [Fact]
    public async Task Sqlite_recovery_v12_production_scanner_traverses_all_candidates_and_excludes_live_and_terminal_rows()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var units = UniqueRuntimeUnits();
        IReadOnlyList<string> first;

        using (var connection = runtime.OpenConnection())
        {
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);

            var source = new DirectSessionSource(connection, units);
            var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
            IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);

            for (var index = 0; index < 173; index++)
            {
                var workflowId = $"recovery-v12-candidate-{index:D4}";
                await liveness.SaveAsync(ProductionRecoveryState(workflowId, index));
                await executions.SaveAsync(WorkflowState(workflowId));
            }

            for (var index = 0; index < 8; index++)
            {
                var workflowId = $"recovery-v12-terminal-{index:D4}";
                await liveness.SaveAsync(ProductionRecoveryState(workflowId, 173 + index));
                await executions.SaveAsync(WorkflowState(workflowId, WorkflowExecutionStatus.Completed));
            }

            for (var index = 0; index < 1_867; index++)
            {
                var workflowId = $"recovery-v12-live-{index:D4}";
                await liveness.SaveAsync(State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddHours(1)));
                await executions.SaveAsync(WorkflowState(workflowId));
            }

            first = await ScanAllProductionPagesAsync(source, scope);
        }

        IReadOnlyList<string> reopened;
        using (var connection = runtime.OpenConnection())
        {
            var source = new DirectSessionSource(connection, units);
            var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            reopened = await ScanAllProductionPagesAsync(source, scope);
        }

        var expected = Enumerable.Range(0, 173).Select(index => $"recovery-v12-candidate-{index:D4}").ToArray();
        Assert.Equal(expected, first);
        Assert.Equal(first, reopened);
    }

    private static async Task<IReadOnlyList<string>> ScanAllProductionPagesAsync(
        DirectSessionSource source,
        TestAccessContextAccessor scope)
    {
        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        for (var pageNumber = 0; pageNumber < 512; pageNumber++)
        {
            var page = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 4, continuationToken: continuation));
            Assert.InRange(page.Items.Count, 0, 4);
            Assert.DoesNotContain(page.Items, candidate =>
                candidate.WorkflowExecutionId.StartsWith("recovery-v12-live-", StringComparison.Ordinal) ||
                candidate.WorkflowExecutionId.StartsWith("recovery-v12-terminal-", StringComparison.Ordinal));
            foreach (var candidate in page.Items)
                Assert.True(seen.Add(candidate.WorkflowExecutionId), $"Duplicate production candidate {candidate.WorkflowExecutionId}.");
            ids.AddRange(page.Items.Select(candidate => candidate.WorkflowExecutionId));
            if (page.NextContinuationToken is null)
                return ids;
            Assert.NotEqual(continuation, page.NextContinuationToken);
            continuation = page.NextContinuationToken;
        }

        throw new Xunit.Sdk.XunitException("The production recovery scanner exceeded the v1.2 bounded page budget.");
    }

    private static ExecutionLivenessState ProductionRecoveryState(string workflowId, int routeIndex) =>
        (routeIndex % 4) switch
        {
            0 => new ExecutionLivenessState(
                "op-1",
                workflowId,
                null,
                null,
                null,
                new InterruptedExecutionState(
                    $"interruption-{workflowId}",
                    workflowId,
                    null,
                    $"checkpoint-{workflowId}",
                    RuntimeInterruptionReason.HostStopped,
                    RuntimeInterruptionStatus.Detected,
                    Now.AddMinutes(-1))),
            1 => State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)),
            2 => new ExecutionLivenessState(
                "op-1",
                workflowId,
                new RuntimeExecutionLease(
                    $"lease-{workflowId}",
                    workflowId,
                    "worker-a",
                    Now.AddMinutes(-6),
                    Now.AddMinutes(10),
                    fencingToken: 1),
                null,
                null,
                null),
            _ => new ExecutionLivenessState(
                "op-1",
                workflowId,
                null,
                new RuntimeHeartbeat(
                    $"heartbeat-{workflowId}",
                    workflowId,
                    "worker-a",
                    null,
                    Now.AddMinutes(-2)),
                null,
                null)
        };

    [Fact]
    public async Task Sqlite_recovery_scan_carries_candidates_from_distinct_routes_across_pages()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);

        await liveness.SaveAsync(State(
            "wf-route-detected",
            "op-1",
            owner: null,
            interrupted: new InterruptedExecutionState(
                "interrupt-route",
                "wf-route-detected",
                null,
                null,
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-6))));
        await liveness.SaveAsync(State("wf-route-lease", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-4)));
        await liveness.SaveAsync(State(
            "wf-route-heartbeat",
            "op-1",
            "worker-a",
            leaseExpiresAt: Now.AddMinutes(5),
            heartbeatRecordedAt: Now.AddMinutes(-3)));
        foreach (var workflowId in new[] { "wf-route-detected", "wf-route-lease", "wf-route-heartbeat" })
            await executions.SaveAsync(WorkflowState(workflowId));

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var request = new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 1);
        var page1 = await scanner.ScanPageAsync(request);
        var page2 = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            page1.NextContinuationToken));
        var page3 = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            page2.NextContinuationToken));

        Assert.Equal("wf-route-detected", Assert.Single(page1.Items).WorkflowExecutionId);
        Assert.InRange(page1.NextContinuationToken?.Length ?? 0, 1, RuntimeStorePageRequest.MaximumContinuationTokenLength);
        Assert.Equal("wf-route-lease", Assert.Single(page2.Items).WorkflowExecutionId);
        Assert.Equal("wf-route-heartbeat", Assert.Single(page3.Items).WorkflowExecutionId);
        Assert.Null(page3.NextContinuationToken);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_preserves_global_order_when_one_route_is_skewed()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);

        // Lease-route rows are intentionally later than the heartbeat-route rows. A route-local page size can
        // otherwise emit the late lease before it has observed the earlier heartbeat frontier.
        await liveness.SaveAsync(State("wf-lease-a", "op-1", "worker-a", leaseExpiresAt: Now.AddSeconds(-1)));
        await liveness.SaveAsync(State("wf-lease-b", "op-1", "worker-a", leaseExpiresAt: Now.AddSeconds(-1)));
        await liveness.SaveAsync(State("wf-heartbeat-a", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(5), heartbeatRecordedAt: Now.AddMinutes(-4)));
        await liveness.SaveAsync(State("wf-heartbeat-b", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(5), heartbeatRecordedAt: Now.AddMinutes(-3)));
        await liveness.SaveAsync(State("wf-heartbeat-c", "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(5), heartbeatRecordedAt: Now.AddMinutes(-2)));
        foreach (var workflowId in new[] { "wf-lease-a", "wf-lease-b", "wf-heartbeat-a", "wf-heartbeat-b", "wf-heartbeat-c" })
            await executions.SaveAsync(WorkflowState(workflowId));

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var ids = new List<string>();
        string? continuation = null;
        do
        {
            var page = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
                Now,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(1),
                1,
                "worker-a",
                continuation));
            ids.AddRange(page.Items.Select(candidate => candidate.WorkflowExecutionId));
            continuation = page.NextContinuationToken;
        }
        while (continuation is not null);

        Assert.Equal(
            ["wf-heartbeat-a", "wf-heartbeat-b", "wf-heartbeat-c", "wf-lease-a", "wf-lease-b"],
            ids);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_keeps_one_page_work_bounded_when_overlap_and_filtered_rows_dominate()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);
        IWorkflowHoldStateStore holds = new GroundworkV2WorkflowHoldStateStore(source, scope);

        for (var index = 0; index < 16; index++)
        {
            var workflowId = index % 2 == 0
                ? $"wf-heavy-terminal-{index:D2}"
                : $"wf-heavy-held-{index:D2}";
            await liveness.SaveAsync(State(
                workflowId,
                "op-1",
                "worker-a",
                leaseExpiresAt: Now.AddMinutes(-100 - index),
                interrupted: new InterruptedExecutionState(
                    $"interrupt-{index}",
                    workflowId,
                    null,
                    null,
                    RuntimeInterruptionReason.HostStopped,
                    RuntimeInterruptionStatus.Detected,
                    Now.AddMinutes(-200 - index))));
            await executions.SaveAsync(WorkflowState(
                workflowId,
                index % 2 == 0 ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running));
            if (index % 2 != 0)
            {
                await holds.SaveAsync(new WorkflowHoldState(
                    $"hold-{index}",
                    workflowId,
                    activeHolds: [WorkflowHold.ForWorkflowExecution(
                        $"hold-entry-{index}",
                        workflowId,
                        Now.AddMinutes(-index),
                        "recovery-test",
                        "held for bounded recovery test")]));
            }
        }

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        source.QueryRequests.Clear();
        var page = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            1));

        Assert.Empty(page.Items);
        Assert.NotNull(page.NextContinuationToken);
        Assert.Equal(4, RecoveryRouteRequests(source).Count);
        Assert.InRange(source.QueryRequests.Count, 3, 7);
        Assert.All(RecoveryRouteRequests(source), request => Assert.Equal(1, request.Paging.Limit));

        source.QueryRequests.Clear();
        var nextPage = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            1,
            continuationToken: page.NextContinuationToken));
        Assert.Empty(nextPage.Items);
        Assert.InRange(source.QueryRequests.Count, 3, 7);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_completes_a_large_one_to_many_hold_correlation_over_pages()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);
        IWorkflowHoldStateStore holds = new GroundworkV2WorkflowHoldStateStore(source, scope);
        var workflowId = "wf-large-hold-correlation";
        await liveness.SaveAsync(State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)));
        await executions.SaveAsync(WorkflowState(workflowId));
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await holds.SaveAsync(new WorkflowHoldState(
                $"hold-{index:D4}",
                workflowId,
                activeHolds: []));
        }

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        source.QueryRequests.Clear();
        var request = new RuntimeRecoveryScanRequest(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            1,
            "worker-a");
        var first = await scanner.ScanPageAsync(request);

        Assert.Empty(first.Items);
        Assert.NotNull(first.NextContinuationToken);
        var firstRecoveryRouteCount = RecoveryRouteRequests(source).Count;
        Assert.Equal(firstRecoveryRouteCount + 3, source.QueryRequests.Count);
        Assert.All(RecoveryRouteRequests(source), query => Assert.Equal(1, query.Paging.Limit));
        var firstHoldQuery = Assert.Single(
            source.QueryRequests,
            query => query.Table.Value.Contains("hold", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit, firstHoldQuery.Paging.Limit);

        source.QueryRequests.Clear();
        var second = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            first.NextContinuationToken));

        Assert.Equal(workflowId, Assert.Single(second.Items).WorkflowExecutionId);
        Assert.Null(second.NextContinuationToken);
        var secondRecoveryRouteCount = RecoveryRouteRequests(source).Count;
        // Continuation pages recheck this exact workflow from the existing workflow index before following the
        // saved inactive-row cursor, so an active hold inserted before that cursor cannot be skipped.
        Assert.Equal(secondRecoveryRouteCount + 4, source.QueryRequests.Count);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_recorrelates_a_pending_candidate_after_its_signal_route_moves()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);
        IWorkflowHoldStateStore holds = new GroundworkV2WorkflowHoldStateStore(source, scope);
        const string workflowId = "wf-pending-route-move";

        await liveness.SaveAsync(State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)));
        await executions.SaveAsync(WorkflowState(workflowId));
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await holds.SaveAsync(new WorkflowHoldState(
                $"hold-{index:D4}",
                workflowId,
                activeHolds: []));
        }

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var request = new RuntimeRecoveryScanRequest(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            1,
            "worker-a");
        var first = await scanner.ScanPageAsync(request);
        Assert.Empty(first.Items);
        Assert.NotNull(first.NextContinuationToken);

        // The pending row was initially canonical to lease expiry. Move it to heartbeat expiry after the route
        // cursor was issued; paging must still find the protected identity and finish its hold walk.
        await liveness.SaveAsync(State(
            workflowId,
            "op-1",
            "worker-a",
            leaseExpiresAt: Now.AddMinutes(5),
            heartbeatRecordedAt: Now.AddMinutes(-2)));
        var second = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            first.NextContinuationToken));
        Assert.Empty(second.Items);
        Assert.NotNull(second.NextContinuationToken);

        var third = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            second.NextContinuationToken));
        Assert.Equal(workflowId, Assert.Single(third.Items).WorkflowExecutionId);
        Assert.Null(third.NextContinuationToken);
    }

    [Fact]
    public async Task Sqlite_recovery_scan_rechecks_new_effective_holds_before_following_a_saved_cursor()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IExecutionLivenessStateStore liveness = new GroundworkV2ExecutionLivenessStateStore(source, scope);
        IWorkflowExecutionStateStore executions = new GroundworkV2WorkflowExecutionStateStore(source, scope);
        IWorkflowHoldStateStore holds = new GroundworkV2WorkflowHoldStateStore(source, scope);
        const string workflowId = "wf-new-effective-hold";

        await liveness.SaveAsync(State(workflowId, "op-1", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)));
        await executions.SaveAsync(WorkflowState(workflowId));
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await holds.SaveAsync(new WorkflowHoldState(
                $"hold-{index:D4}",
                workflowId,
                activeHolds: []));
        }

        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope, continuationCodec: TestCodec());
        var request = new RuntimeRecoveryScanRequest(
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            1,
            "worker-a");
        var first = await scanner.ScanPageAsync(request);
        Assert.Empty(first.Items);
        Assert.NotNull(first.NextContinuationToken);

        // This ID sorts before the saved hold cursor. The bounded current-payload recheck must see it even though
        // following the provider cursor alone would start after the new active row.
        await holds.SaveAsync(new WorkflowHoldState(
            "hold-0000-active",
            workflowId,
            activeHolds: [WorkflowHold.ForWorkflowExecution(
                "hold-entry-active",
                workflowId,
                Now,
                "recovery-test",
                "new active hold")]));

        var second = await scanner.ScanPageAsync(new RuntimeRecoveryScanRequest(
            request.Now,
            request.LeaseTimeout,
            request.HeartbeatTimeout,
            request.Limit,
            request.OwnerId,
            first.NextContinuationToken));
        Assert.Empty(second.Items);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task Sqlite_reads_a_pre_recovery_projection_workflow_hold_row()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var holdUnit = units[ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind];
        var state = new WorkflowHoldState("legacy-hold", "wf-legacy-hold");

        // This is the row shape written before the recovery-only effective-hold experiment: identity, content,
        // schema version, collection, and workflow projections only.
        var legacyValues = GroundworkV2WorkflowHoldStateStorageConventions.Values(state);
        source.Open(
                holdUnit.Id.Value,
                StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Insert(legacyValues, WriteOptions.CreateOnly);

        var holds = new GroundworkV2WorkflowHoldStateStore(source, scope);
        var loaded = await holds.FindAsync(state.ControlPlaneStateId);

        Assert.Equal(state.ControlPlaneStateId, loaded?.ControlPlaneStateId);
        Assert.Equal(state.WorkflowExecutionId, loaded?.WorkflowExecutionId);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.CollectionField
            ],
            holdUnit.Columns
                .Select(column => column.Name)
                .Except([
                    ElsaRuntimeV2StorageManifest.IdField,
                    ElsaRuntimeV2StorageManifest.SchemaVersionField,
                    ElsaRuntimeV2StorageManifest.ContentField,
                    ElsaRuntimeV2StorageManifest.VersionField
                ]));
    }

    private static async Task AssertStoreBehaviorAsync(NativeProviderRuntime runtime)
    {
        using var connection = runtime.OpenConnection();
        var units = UniqueRuntimeUnits();
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, units);
        var scopeA = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopeB = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        IExecutionLivenessStateStore storeA = new GroundworkV2ExecutionLivenessStateStore(source, scopeA);
        IExecutionLivenessStateStore storeB = new GroundworkV2ExecutionLivenessStateStore(source, scopeB);
        IWorkflowExecutionStateStore workflowExecutionStore = new GroundworkV2WorkflowExecutionStateStore(source, scopeA);
        IIncidentStateStore incidentStore = new GroundworkV2IncidentStateStore(source, scopeA);
        ISchedulerStateStore schedulerStore = new GroundworkV2SchedulerStateStore(source, scopeA);
        IWorkflowHoldStateStore workflowHoldStore = new GroundworkV2WorkflowHoldStateStore(source, scopeA);

        await storeA.SaveAsync(State("wf-1", "op-2", owner: "worker-a"));
        await storeA.SaveAsync(State("wf-1", "op-1", owner: "worker-a"));
        await storeA.SaveAsync(State("wf-2", "op-1", owner: "worker-a"));
        Assert.Null(await storeB.FindAsync("wf-1", "op-1"));

        var found = await storeA.FindVersionedAsync("wf-1", "op-1");
        Assert.NotNull(found);
        Assert.Equal(1, found!.Revision);

        var page = await storeA.ListPageAsync(new ExecutionLivenessStatePageQuery("wf-1", 1));
        Assert.Equal(["op-1"], page.Items.Select(state => state.OperationalStateId));
        Assert.NotNull(page.NextContinuationToken);
        var next = await storeA.ListPageAsync(new ExecutionLivenessStatePageQuery("wf-1", 1, page.NextContinuationToken));
        Assert.Equal(["op-2"], next.Items.Select(state => state.OperationalStateId));

        var all = await storeA.ListAllPageAsync(new RuntimeStorePageRequest(10));
        Assert.Equal(["wf-1/op-1", "wf-1/op-2", "wf-2/op-1"], all.Items.Select(state => $"{state.WorkflowExecutionId}/{state.OperationalStateId}"));

        var replacement = State("wf-1", "op-1", owner: "worker-b", metadata: new Dictionary<string, string> { ["value"] = "replacement" });
        var saved = await storeA.TrySaveAsync(replacement, found.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, saved.Status);
        Assert.True(saved.Succeeded);
        Assert.Equal(2, saved.Revision);
        Assert.Equal("replacement", (await storeA.FindAsync("wf-1", "op-1"))!.Metadata["value"]);

        var stale = await storeA.TrySaveAsync(State("wf-1", "op-1", owner: "stale"), found.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, stale.Status);
        var createConflict = await storeA.TrySaveAsync(State("wf-1", "op-1", owner: "create"), expectedRevision: 0);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, createConflict.Status);
        var missing = await storeA.TrySaveAsync(State("wf-1", "missing", owner: "missing"), expectedRevision: 1);
        Assert.Equal(ExecutionLivenessStateWriteStatus.NotFound, missing.Status);

        await storeA.SaveAsync(State(
            "wf-recovery",
            "op-detected",
            owner: null,
            interrupted: new InterruptedExecutionState(
                "interrupt-1",
                "wf-recovery",
                leaseId: null,
                lastCheckpointId: "checkpoint-1",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-3))));
        await storeA.SaveAsync(State("wf-recovery", "op-lease", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)));
        await storeA.SaveAsync(State("wf-recovery", "op-heartbeat", "worker-a", leaseExpiresAt: Now.AddMinutes(5), heartbeatRecordedAt: Now.AddMinutes(-2)));
        await workflowExecutionStore.SaveAsync(WorkflowState("wf-recovery"));
        await incidentStore.SaveAsync(new IncidentState(
            "incident-1",
            "wf-recovery",
            null,
            null,
            IncidentSeverity.Error,
            IncidentStatus.Open,
            null,
            "failure",
            "recovery test incident",
            Now.AddMinutes(-5),
            null));
        await schedulerStore.SaveAsync(new SchedulerState("wf-recovery", 1));
        await workflowHoldStore.SaveAsync(new WorkflowHoldState("hold-recovery", "wf-recovery"));

        source.QueryRequests.Clear();
        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scopeA, continuationCodec: TestCodec());
        var candidates = await scanner.ScanAsync(new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10));
        Assert.Equal(
            ["op-detected", "op-heartbeat", "op-lease"],
            candidates.Select(candidate => candidate.OperationalStateId));
        Assert.Equal(RuntimeInterruptionReason.HostStopped, candidates.First().Reason);
        Assert.Equal("Running", candidates.First().Metadata["runtime.recovery.correlation.execution"]);
        Assert.Equal("true", candidates.First().Metadata["runtime.recovery.correlation.incident"]);
        Assert.Equal("true", candidates.First().Metadata["runtime.recovery.correlation.scheduler"]);
        Assert.Equal("false", candidates.First().Metadata["runtime.recovery.correlation.hold"]);

        AssertUnfilteredRecoveryRoutes(source, 10);

        source.QueryRequests.Clear();
        var ownerCandidates = await scanner.ScanAsync(new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10, "worker-a"));
        Assert.Equal(["op-detected", "op-heartbeat", "op-lease"], ownerCandidates.Select(candidate => candidate.OperationalStateId));

        AssertOwnerRecoveryRoutes(source, 10);
    }

    private static IReadOnlyDictionary<string, StorageUnit> UniqueRuntimeUnits()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return ElsaRuntimeV2StorageManifest.CreateUnits()
            .Where(unit => unit.Id.Value is
                ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind or
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind or
                ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind or
                ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind or
                ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind)
            .ToDictionary(
                unit => unit.Id.Value,
                unit => unit with
                {
                    Id = new StorageUnitId($"{unit.Id.Value}-{suffix}"),
                    Name = $"{unit.Name}_{suffix}"
                },
                StringComparer.Ordinal);
    }

    private static void AssertUnfilteredRecoveryRoutes(DirectSessionSource source, int limit)
    {
        var requests = RecoveryRouteRequests(source);
        Assert.Equal(4, requests.Count);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField
            ],
            requests.Select(request => request.Order[0].Column.Name));
        Assert.All(requests, request => Assert.Equal(1, request.Paging.Limit));
        Assert.Equal(
            [
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField }
            ],
            requests.Select(request => PredicateColumns(request.Where).OrderBy(column => column, StringComparer.Ordinal).ToArray()));
        var detectedPredicate = Assert.IsType<QueryPredicate.Equal>(requests[0].Where);
        Assert.Equal(QueryType.Int32, detectedPredicate.Value.Type);
        Assert.IsType<int>(detectedPredicate.Value.Value);
    }

    private static void AssertOwnerRecoveryRoutes(DirectSessionSource source, int limit)
    {
        var requests = RecoveryRouteRequests(source);
        Assert.Equal(6, requests.Count);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField
            ],
            requests.Select(request => request.Order[0].Column.Name));
        Assert.All(requests, request => Assert.Equal(1, request.Paging.Limit));
        Assert.Equal(
            [
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray()
            ],
            requests.Select(request => PredicateColumns(request.Where).OrderBy(column => column, StringComparer.Ordinal).ToArray()));
        Assert.All(
            requests.Take(3),
            request =>
            {
                var detected = EqualityFor(request.Where, ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField);
                Assert.Equal(QueryType.Int32, detected.Value.Type);
                Assert.IsType<int>(detected.Value.Value);
        });
    }

    private static IReadOnlyList<QueryRequest> RecoveryRouteRequests(DirectSessionSource source) =>
        source.QueryRequests
            .Where(request => request.Order[0].Column.Name is
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField or
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField or
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField or
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField)
            .ToArray();

    private static IReadOnlyCollection<string> PredicateColumns(QueryPredicate predicate) => predicate switch
    {
        QueryPredicate.Equal equal => [equal.Column.Name],
        QueryPredicate.Range range => [range.Column.Name],
        QueryPredicate.And and => and.Terms.SelectMany(PredicateColumns).ToArray(),
        _ => []
    };

    private static QueryPredicate.Equal EqualityFor(QueryPredicate predicate, string field)
    {
        if (TryEqualityFor(predicate, field, out var equal))
            return equal!;

        throw new InvalidOperationException($"Predicate did not contain equality for '{field}'.");
    }

    private static bool TryEqualityFor(QueryPredicate predicate, string field, out QueryPredicate.Equal? equality)
    {
        if (predicate is QueryPredicate.Equal equal && StringComparer.Ordinal.Equals(equal.Column.Name, field))
        {
            equality = equal;
            return true;
        }

        if (predicate is QueryPredicate.And and)
            foreach (var term in and.Terms)
                if (TryEqualityFor(term, field, out equality))
                    return true;

        equality = null;
        return false;
    }

    private static ExecutionLivenessState State(
        string workflowExecutionId,
        string operationalStateId,
        string? owner,
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? heartbeatRecordedAt = null,
        InterruptedExecutionState? interrupted = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            operationalStateId,
            workflowExecutionId,
            owner is null
                ? null
                : new RuntimeExecutionLease(
                    $"lease-{operationalStateId}",
                    workflowExecutionId,
                    owner,
                    leaseExpiresAt is { } expires && expires <= Now
                        ? expires.AddMinutes(-1)
                        : Now.AddMinutes(-1),
                    leaseExpiresAt ?? Now.AddMinutes(5),
                    fencingToken: 1),
            owner is null
                ? null
                : new RuntimeHeartbeat(
                    $"heartbeat-{operationalStateId}",
                    workflowExecutionId,
                    owner,
                    $"lease-{operationalStateId}",
                    heartbeatRecordedAt ?? Now,
                    metadata),
            drain: null,
            interrupted,
            metadata: metadata);

    private static WorkflowExecutionState WorkflowState(
        string workflowExecutionId,
        WorkflowExecutionStatus status = WorkflowExecutionStatus.Running) =>
        new(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "hash-1"),
            status,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static IRuntimeRecoveryContinuationCodec TestCodec(string key = "groundwork-recovery-continuation-key-32-bytes") =>
        new HmacTestRecoveryContinuationCodec(key);

    private sealed class HmacTestRecoveryContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        private readonly byte[] key;

        public HmacTestRecoveryContinuationCodec(string key) => this.key = Encoding.UTF8.GetBytes(key);

        public string Encode(string purpose, ReadOnlySpan<byte> payload)
        {
            var payloadBytes = payload.ToArray();
            return $"{purpose}.{Base64Url(payloadBytes)}.{Base64Url(HMACSHA256.HashData(key, SigningInput(purpose, payloadBytes)))}";
        }

        public byte[] Decode(string purpose, string token)
        {
            var parts = token.Split('.');
            if (parts is not [var tokenPurpose, var payloadPart, var signaturePart] ||
                !StringComparer.Ordinal.Equals(tokenPurpose, purpose))
            {
                throw new ArgumentException("Invalid test continuation.", nameof(token));
            }

            var payload = FromBase64Url(payloadPart);
            var signature = FromBase64Url(signaturePart);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, SigningInput(purpose, payload))))
                throw new ArgumentException("Invalid test continuation.", nameof(token));
            return payload;
        }

        private static byte[] SigningInput(string purpose, ReadOnlySpan<byte> payload)
        {
            var purposeBytes = Encoding.UTF8.GetBytes(purpose);
            var input = new byte[purposeBytes.Length + 1 + payload.Length];
            purposeBytes.CopyTo(input, 0);
            input[purposeBytes.Length] = (byte)'.';
            payload.CopyTo(input.AsSpan(purposeBytes.Length + 1));
            return input;
        }

        private static string Base64Url(ReadOnlySpan<byte> value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
        }
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, IReadOnlyDictionary<string, StorageUnit> units) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<(string UnitId, StorageAccess Access), IStorageSession> sessions = [];

        public List<QueryRequest> QueryRequests { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            var unit = Assert.Single(units.Values, candidate => candidate.Id.Value == unitId);
            var key = (unitId, access);
            if (sessions.TryGetValue(key, out var session))
                return session;

            session = new RecordingSession(connection.OpenSession(unit, access), QueryRequests);
            sessions.Add(key, session);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            return Assert.Contains(unitId, units);
        }
    }

    private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;

        public StoredEntry? Read(StorageKey key) => inner.Read(key);

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return inner.Query(request, options);
        }

        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);

        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);
    }

    private sealed class NativeProviderRuntime(string providerName, string connectionString, string? sqlitePath) : IAsyncDisposable
    {
        public static NativeProviderRuntime Create(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null);

            var path = Path.Combine(Path.GetTempPath(), $"elsa-runtime-liveness-{Guid.NewGuid():N}.db");
            return new(providerName, $"Data Source={path}", path);
        }

        public IStorageProviderConnection OpenConnection() => providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

        public ValueTask DisposeAsync()
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
