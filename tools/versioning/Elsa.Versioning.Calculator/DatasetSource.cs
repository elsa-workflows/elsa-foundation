namespace Elsa.Versioning.Calculator;

/// <summary>Where a commit's dependency-map dataset comes from (spec 259).</summary>
public enum DatasetSource
{
    /// <summary>The committed <c>docs/maps/dependency-map.json</c> when the commit has one, else derived from its tree.</summary>
    Auto,

    /// <summary>The committed file only; a commit without one is refused.</summary>
    Committed,

    /// <summary>Derived from the commit's tree by the shared project-graph scan, even when a committed file exists.</summary>
    Derived
}
