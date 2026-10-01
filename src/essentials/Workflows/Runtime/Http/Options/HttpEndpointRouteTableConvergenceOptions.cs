namespace Elsa.Workflows.Runtime.Http.Options;

/// <summary>
/// Tuning for the check that brings each node's HTTP route table in line with endpoints published, and HTTP bookmarks
/// created or consumed, on other nodes (#2190).
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

    /// <summary>Refuses a non-positive interval, which would make the check spin or never run.</summary>
    public void Validate()
    {
        if (Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Interval), Interval, "The HTTP route-table convergence interval must be positive.");
        if (MaxBackoffInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaxBackoffInterval), MaxBackoffInterval, "The HTTP route-table convergence backoff ceiling must be positive.");
    }
}
