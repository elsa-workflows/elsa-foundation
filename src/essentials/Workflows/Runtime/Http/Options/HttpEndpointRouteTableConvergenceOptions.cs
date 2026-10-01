namespace Elsa.Workflows.Runtime.Http.Options;

/// <summary>
/// Tuning for the check that brings each node's HTTP route table in line with endpoints published, and HTTP bookmarks
/// created or consumed, on other nodes (#2190). <c>WorkflowsRuntimeHttpFeature</c> validates its settings once, when
/// it composes these options; a host configuring them directly owns their validity.
/// </summary>
public sealed class HttpEndpointRouteTableConvergenceOptions
{
    /// <summary>
    /// Time between checks while healthy, and so the bound on how long an endpoint published on another node can keep
    /// returning 404 here. A check reads only the stimulus identities of the HTTP bindings and waiting bookmarks; the
    /// route table is rebuilt only when they changed.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound the interval widens to while checks keep failing, for example while the database is down.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(1);
}
