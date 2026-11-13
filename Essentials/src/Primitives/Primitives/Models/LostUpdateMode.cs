namespace Elsa.Primitives.Models;

/// <summary>
/// How a step's write on a stale base is handled (spec 1221, ADR 0092 decision 6). An activity type declares it, or the
/// author sets it per node; the publish compiler pins it in the executable.
/// </summary>
public enum LostUpdateMode
{
    /// <summary>Light tracking: versions decide; a lost update applies (last write wins) with a lost-update warning.</summary>
    Default = 0,

    /// <summary>Strict read-modify-write: stored digests decide on a version mismatch; a real lost update faults the step.</summary>
    Fault = 1
}
