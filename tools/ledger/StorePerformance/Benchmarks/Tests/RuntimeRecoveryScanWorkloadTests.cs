using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeRecoveryScanWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_recovery_contract_and_literal_golden_digest()
    {
        var adapter = new InMemoryRecoveryAdapter();
        var result = await new RuntimeRecoveryScanWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeRecoveryScanWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeRecoveryScanWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.Get(RuntimeRecoveryScanWorkload.WorkloadId).OperationSequence,
            result.ObservableOperations);
        Assert.Equal(2, adapter.Opened.Count);
        Assert.Equal(2, adapter.Opened.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public async Task Prepares_only_the_three_bounded_scan_operations_after_setup()
    {
        var adapter = new InMemoryRecoveryAdapter();
        var workload = new RuntimeRecoveryScanWorkload();
        await workload.ExecuteAsync(adapter);
        var operations = await workload.PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(
            ["scan-recovery-candidates", "reopen-and-rescan", "verify-bounded-order-and-non-candidates"],
            operations.Select(operation => operation.Id));
    }

    private sealed class InMemoryRecoveryAdapter : IRuntimeRecoveryScanWorkloadAdapter
    {
        private readonly InMemoryExecutionLivenessStateStore liveness = new();
        private readonly InMemoryWorkflowExecutionStateStore executions = new();
        private readonly InMemoryIncidentStateStore incidents = new();
        private readonly InMemorySchedulerStateStore scheduler = new();
        private readonly InMemoryWorkflowHoldStateStore holds = new();

        public List<IRuntimeRecoveryScanner> Opened { get; } = [];
        public string PersistenceScope => "recovery-scan";

        public ValueTask<RuntimeRecoveryScanClient> OpenClientAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Open(cancellationToken));

        public ValueTask<RuntimeRecoveryScanClient> ReopenClientAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Open(cancellationToken));

        private RuntimeRecoveryScanClient Open(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scanner = new TerminalFilteringRecoveryScanner(new InMemoryRuntimeRecoveryScanner(liveness));
            Opened.Add(scanner);
            return new RuntimeRecoveryScanClient(scanner, liveness, executions, incidents, scheduler, holds);
        }
    }

    private sealed class TerminalFilteringRecoveryScanner(IRuntimeRecoveryPagedScanner inner) : IRuntimeRecoveryPagedScanner
    {
        public bool SupportsPaging => true;

        public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
            RuntimeRecoveryScanRequest request,
            CancellationToken cancellationToken = default)
        {
            var page = await ScanPageAsync(request, cancellationToken);
            return page.Items;
        }

        public async ValueTask<RuntimeRecoveryPage> ScanPageAsync(
            RuntimeRecoveryScanRequest request,
            CancellationToken cancellationToken = default)
        {
            var items = new List<RuntimeRecoveryCandidate>(request.Limit);
            var continuation = request.ContinuationToken;
            do
            {
                var page = await inner.ScanPageAsync(
                    new RuntimeRecoveryScanRequest(
                        request.Now,
                        request.LeaseTimeout,
                        request.HeartbeatTimeout,
                        request.Limit,
                        request.OwnerId,
                        continuation),
                    cancellationToken);
                items.AddRange(page.Items.Where(candidate =>
                    !candidate.WorkflowExecutionId.StartsWith("terminal-", StringComparison.Ordinal) &&
                    !candidate.WorkflowExecutionId.StartsWith("live-", StringComparison.Ordinal)));
                continuation = page.NextContinuationToken;
            }
            while (items.Count < request.Limit && continuation is not null);

            return new RuntimeRecoveryPage(request, items.Take(request.Limit).ToArray(), continuation);
        }
    }
}
