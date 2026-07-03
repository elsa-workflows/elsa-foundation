namespace Elsa.Expressions.JavaScript.Jint.Options;

public sealed class FeatureOptions
{
    public bool AllowClrAccess { get; set; }

    public TimeSpan? ScriptCacheTimeout { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Sandbox limit (DS-9): the wall-clock execution timeout applied to a single script evaluation via
    /// a Jint timeout constraint. A pathological script (e.g. an infinite loop) is aborted once this
    /// elapses instead of hanging the executing thread. <c>null</c> disables the timeout constraint.
    /// Defaults to a generous 5 seconds — long enough for legitimate scripts, short enough to bound a
    /// runaway one.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sandbox limit (DS-9): the maximum number of statements a single evaluation may execute before a
    /// Jint constraint aborts it. Guards against runaway loops even when a timeout is not reached first.
    /// <c>null</c> or a non-positive value disables the statement-count constraint. Default 10,000,000.
    /// </summary>
    public int? MaxStatements { get; set; } = 10_000_000;

    /// <summary>
    /// Sandbox limit (DS-9): the maximum call-stack recursion depth for a single evaluation. Guards
    /// against unbounded recursion blowing the stack. <c>null</c> or a non-positive value disables the
    /// recursion constraint. Default 300.
    /// </summary>
    public int? MaxRecursionDepth { get; set; } = 300;
}