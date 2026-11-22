namespace Elsa.Cluster.Core.Options;

/// <summary>Settings of the shared dormancy check.</summary>
public sealed class SchemaDormancyOptions
{
    /// <summary>
    /// How old this host's observation of a family may be before the check reads the family's record again rather than
    /// report a requirement unmet. Short, so a request finalization already allows is not refused on a stale
    /// view, and a bound, so a burst of refused requests costs at most one read per interval. Defaults to two seconds;
    /// the finalization gate's own refresh runs every fifteen.
    /// </summary>
    public TimeSpan RefreshBound { get; set; } = TimeSpan.FromSeconds(2);
}
