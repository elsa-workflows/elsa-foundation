using System.Collections.Concurrent;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// Decides whether this member may claim or renew placement of an execution (spec 184, invariant 1). Both claim paths
/// ask it before they claim: the actor provider's activation and the placement pump's backlog sweep, and the pump asks
/// again before it renews.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claimant checks itself</b> (FR-011, mechanism 1). The requirement is evaluated with
/// <see cref="IRuntimeRequirementChecker"/> against this shell's registries as they are at that moment: the evaluation
/// the artifact reconciler and Publishing's preflight already use. The member's own published runnability entry is never
/// consulted, so a report that lags can mislead a diagnostic but can never place work.
/// </para>
/// <para>
/// <b>Standing</b> (FR-012). A member that is joining, draining or has left claims nothing, and neither does a shell
/// whose join sweep has not completed in this process. A lapsed member keeps claiming, subject to its own requirement
/// check (FR-026): stopping it would turn a membership outage into an execution outage.
/// </para>
/// <para>
/// Spec 181's refusal of writes to a Runtime family (FR-012) is not evaluated: spec 181 is not built yet, and this gate
/// is where that exclusion belongs once it is.
/// </para>
/// </remarks>
public sealed class ExecutionPlacementGate
{
    private const int MaximumRememberedRefusals = 10_000;
    private readonly IClusterMembership _membership;
    private readonly JoinSweepLedger _ledger;
    private readonly DistributedRuntimeShell _shell;
    private readonly ExecutionPlacementRequirementResolver _resolver;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, string> _loggedRefusals = new(StringComparer.Ordinal);

    public ExecutionPlacementGate(
        IClusterMembership membership,
        JoinSweepLedger ledger,
        DistributedRuntimeShell shell,
        ExecutionPlacementRequirementResolver resolver,
        ILogger<ExecutionPlacementGate>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(resolver);
        _membership = membership;
        _ledger = ledger;
        _shell = shell;
        _resolver = resolver;
        _logger = logger ?? NullLogger<ExecutionPlacementGate>.Instance;
    }

    public IClusterMembership Membership => _membership;

    public JoinSweepLedger Ledger => _ledger;

    public DistributedRuntimeShell Shell => _shell;

    /// <summary>The routing identity: the local member's host id (FR-001).</summary>
    public string HostId => _membership.GetLocalStanding().Identity.HostId;

    /// <summary>Whether this shell's join sweep has completed in this process (FR-022).</summary>
    public bool HasCompletedJoinSweep => _ledger.IsComplete(HostId, _shell.Name);

    /// <summary>Why this member may not claim anything now, or <see langword="null"/> when its standing allows it (FR-012).</summary>
    public PlacementDecision? CheckStanding()
    {
        var standing = _membership.GetLocalStanding();
        if (standing.Status != MemberStatus.Active)
            return PlacementDecision.Refused(PlacementRefusalKind.MemberNotActive, $"this member is {standing.Status.ToString().ToLowerInvariant()}");
        return _ledger.IsComplete(standing.Identity.HostId, _shell.Name)
            ? null
            : PlacementDecision.Refused(PlacementRefusalKind.JoinSweepPending, "this member has not finished reclaiming its predecessor's leases");
    }

    /// <summary>
    /// Decides whether this member may claim or renew placement of <paramref name="workflowExecutionId"/> now, in the
    /// operation scope <paramref name="services"/>. <paramref name="inHand"/> is the command being dispatched, if any,
    /// and <paramref name="pending"/> reads the commands the transport holds for it: a start command among them supplies
    /// the pin of an execution that has no durable state yet (FR-016).
    /// </summary>
    public async ValueTask<PlacementDecision> DecideAsync(
        string workflowExecutionId,
        IServiceProvider services,
        IEnumerable<WorkflowExecutionCommandEnvelope> inHand,
        Func<CancellationToken, ValueTask<IReadOnlyList<WorkflowExecutionCommandEnvelope>>>? pending = null,
        CancellationToken cancellationToken = default)
    {
        if (CheckStanding() is { } refused)
            return refused;

        var requirement = await _resolver.ResolveAsync(workflowExecutionId, services, inHand, pending, cancellationToken);
        return Check(requirement, services);
    }

    /// <summary>
    /// Logs a refusal once per execution and reason, not on every sweep (FR-029): a claim refused for its requirement at
    /// information level with the execution id and what is unmet or unresolved, and a refusal for the member's standing
    /// at debug level, because it concerns every execution alike.
    /// </summary>
    public void Report(string workflowExecutionId, WorkflowExecutionPartition partition, PlacementDecision decision)
    {
        if (decision.IsRunnable)
        {
            _loggedRefusals.TryRemove(Key(workflowExecutionId, partition), out _);
            return;
        }

        if (!decision.IsAboutRequirement)
        {
            _logger.LogDebug("Not claiming workflow execution {WorkflowExecutionId}: {Reason}.", workflowExecutionId, decision.Reason);
            return;
        }

        var key = Key(workflowExecutionId, partition);
        if (_loggedRefusals.TryGetValue(key, out var logged) && string.Equals(logged, decision.Reason, StringComparison.Ordinal))
            return;

        if (_loggedRefusals.Count >= MaximumRememberedRefusals)
            _loggedRefusals.Clear();
        _loggedRefusals[key] = decision.Reason!;
        _logger.LogInformation(
            "Not claiming workflow execution {WorkflowExecutionId} on this member: {Reason}. Its commands wait in the durable transport for a member that can run it.",
            workflowExecutionId,
            decision.Reason);
    }

    private static PlacementDecision Check(ExecutionPlacementRequirement requirement, IServiceProvider services)
    {
        if (!requirement.IsResolved)
            return PlacementDecision.Refused(PlacementRefusalKind.RequirementUnresolved, $"its placement requirement cannot be resolved: {requirement.UnresolvedReason}");
        if (requirement.Subject is not { } subject)
            return PlacementDecision.Runnable([]);

        var check = services.GetRequiredService<IRuntimeRequirementChecker>().Check(subject);
        var requirements = ExecutionPlacementRequirement.ToMemberRequirements(check);
        if (check.ActivityTypes.Any(entry => string.IsNullOrWhiteSpace(entry.TypeAlias)))
        {
            var nodes = string.Join(", ", check.ActivityTypes.Where(entry => string.IsNullOrWhiteSpace(entry.TypeAlias)).SelectMany(entry => entry.NodeIds));
            return PlacementDecision.Refused(
                PlacementRefusalKind.RequirementUnresolved,
                $"its placement requirement cannot be resolved: the activity type of node(s) {nodes} in pinned executable '{subject.ArtifactId}' cannot be read",
                requirements);
        }

        return check.IsSatisfied
            ? PlacementDecision.Runnable(requirements)
            : PlacementDecision.Refused(PlacementRefusalKind.RequirementUnmet, Describe(check), requirements);
    }

    /// <summary>What this shell's runtime lacks, in the words of the membership requirement kinds (FR-006).</summary>
    private static string Describe(RuntimeRequirementCheckResult check)
    {
        var unmet = check.Requirements
            .Where(entry => entry.Status != RuntimeRequirementStatus.Available)
            .Select(entry => (object)new ActivatesRuntimeConsumer(entry.ConsumerKey, entry.SchemaVersion))
            .Concat(check.StorageDrivers.Where(entry => entry.Status != RuntimeRequirementStatus.Available).Select(entry => new HasStorageDriver(entry.DriverKey)))
            .Concat(check.ActivityTypes.Where(entry => entry.Status != RuntimeRequirementStatus.Available).Select(entry => new ResolvesActivityType(entry.TypeAlias)))
            .Select(requirement => requirement.ToString());
        return $"this member's runtime does not satisfy its placement requirement: it needs a member that {string.Join("; ", unmet)}";
    }

    private static string Key(string workflowExecutionId, WorkflowExecutionPartition partition) => $"{partition.Value}\u001f{workflowExecutionId}";
}
