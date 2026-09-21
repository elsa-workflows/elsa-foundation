namespace Elsa.Workflows.Runtime.Core.Constants;

public static class ActivityOutcomes
{
    public const string Done = "Done";

    /// <summary>
    /// Emitted by boolean branch activities (e.g. <c>If</c>) when the evaluated condition is true.
    /// </summary>
    public const string True = "True";

    /// <summary>
    /// Emitted by boolean branch activities (e.g. <c>If</c>) when the evaluated condition is false.
    /// </summary>
    public const string False = "False";

    /// <summary>
    /// Emitted by multi-way branch activities (e.g. <c>Switch</c>) when no case matches the evaluated
    /// value and the default branch is taken.
    /// </summary>
    public const string Default = "Default";

    /// <summary>
    /// Emitted by the <c>Break</c> leaf (#299) to exit the enclosing loop early. The four loop composites
    /// (<c>For</c>/<c>ForEach</c>/<c>While</c>/<c>Do</c>) detect this outcome on a completed body and end
    /// the loop with <see cref="Done"/> instead of scheduling the next pass. Matched by name so the loops
    /// take no dependency on the (separate) Break activity module.
    /// </summary>
    public const string Break = "Break";
}