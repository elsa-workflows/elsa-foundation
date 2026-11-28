namespace Elsa.Workflows.Runtime.Http.Options;

/// <summary>
/// Tuning for the check that brings each node's HTTP route table in line with reservations committed on other nodes.
/// <c>WorkflowsRuntimeStandaloneHttpFeature</c> validates its settings when
/// it composes these options; a host configuring them directly owns their validity.
/// </summary>
public sealed class HttpEndpointRouteTableConvergenceOptions
{
    /// <summary>
    /// Time between healthy checks for changes to the durable HTTP reservations. The route table is rebuilt when
    /// their fingerprint changes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound the interval widens to while checks keep failing, for example while the database is down.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(1);
}
