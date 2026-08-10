using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Shared rig for the ADR 0064 conformance corpus: builds a routing scenario, runs it on the real engine, and
/// reports the two things the corpus asserts — the <b>order</b> nodes executed in and the terminal workflow
/// status. Kept separate from the scenarios themselves so the corpus file reads as a table of graphs and
/// expectations rather than a pile of harness plumbing.
/// </summary>
internal static class FlowchartConformanceCorpus
{
    /// <summary>
    /// The Flowchart composite's own node id, excluded from the observed order — the corpus is about which
    /// children ran, not about the container.
    /// </summary>
    private const string ContainerNodeId = "node-flowchart";

    /// <summary>
    /// Deterministic activity-execution ids, zero-padded so ordinal sort <b>is</b> hand-out order. The harness's
    /// id generator dequeues one per activity execution, so sorting the persisted states by this id recovers the
    /// order the engine actually ran them in — an observation that belongs to neither join policy and therefore
    /// stays valid as the instrument across the A/B.
    /// </summary>
    private static IEnumerable<string> ExecutionIdPool => Enumerable.Range(0, 96).Select(index => $"actexec-{index:D2}");

    public static async Task<CorpusRun> RunAsync(
        string joinPolicyKind,
        Func<FlowchartRuntimeFixture, WorkflowExecutable> buildExecutable,
        params IFlowchartPolicy[] policies)
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ExecutionIdPool,
            services =>
            {
                // The A/B is a configuration flip on the registry both semantics already live in — the corpus
                // runs one engine twice, not two engines.
                services.AddSingleton(new FlowchartOptions { JoinPolicyKind = joinPolicyKind });
                foreach (var policy in policies)
                    services.AddSingleton<IFlowchartPolicy>(policy);
                // A corpus loop runs more drain cycles than the default 64 allows; the limit exists to catch
                // runaway drains, not to bound a legitimate multi-iteration loop.
                services.AddSingleton(new WorkflowDrainOrchestratorOptions(maxDrainCycles: 4096));
            });

        await fixture.ExecuteAsync(buildExecutable(fixture));

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        var order = states
            .Where(state => !StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, ContainerNodeId))
            .OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .Select(state => state.Execution.ExecutableNodeId)
            .ToArray();
        var workflowState = await fixture.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        return new CorpusRun(order, workflowState?.Status, await TryReadFlowchartStateAsync(fixture));
    }

    // A Flowchart that completes closes its private state by contract, so the last committed blob is the only
    // reading available — and for a run that faults before the composite ever staged state, there is none at all.
    // The corpus treats the blob as optional evidence: scenarios assert order and status first, state second.
    private static async Task<FlowchartExecutionState?> TryReadFlowchartStateAsync(FlowchartRuntimeFixture fixture)
    {
        try
        {
            return await fixture.GetFlowchartStateAsync();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>What a corpus scenario observes: the ordered node executions, the terminal workflow status, and
    /// (when still readable) the persisted Flowchart state.</summary>
    public sealed record CorpusRun(
        IReadOnlyList<string> Order,
        WorkflowExecutionStatus? Status,
        FlowchartExecutionState? FlowchartState);

    /// <summary>
    /// A policy node that schedules one caller-scripted target per invocation — the corpus's stand-in for a
    /// gate whose decision changes between loop iterations. A probe leaf emits one fixed outcome for the life
    /// of the graph, so alternating a branch across iterations has to come from a policy.
    /// </summary>
    public sealed class ScriptedPolicy(string policyKind, params string[] targetNodeIds) : IFlowchartPolicy
    {
        private readonly Queue<string> _targets = new(targetNodeIds);

        public string PolicyKind { get; } = policyKind;
        public string DisplayName => $"Scripted ({PolicyKind})";

        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context)
        {
            if (!_targets.TryDequeue(out var target))
                throw new InvalidOperationException($"Scripted policy '{PolicyKind}' ran out of scripted targets.");

            return new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: target)]);
        }
    }
}
