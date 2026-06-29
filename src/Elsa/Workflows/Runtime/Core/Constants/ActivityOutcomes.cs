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
}