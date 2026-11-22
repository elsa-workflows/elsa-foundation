namespace Elsa.Expressions.JavaScript.Jint.Options;

public sealed class FeatureOptions
{
    /// <summary>
    /// Requested evaluation-thread stack size in bytes. Default 16 MiB; supported range 16–64 MiB.
    /// Raising this reserves more address space; it does not raise the execution budget.
    /// </summary>
    public int EvaluationThreadStackBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Stack budget in bytes, checked at statements, calls and host-function entries. Default 512 KiB;
    /// supported range 64–512 KiB. The Jint adapter enforces the ceiling independently of host configuration
    /// to preserve its unwind reserve. Native recursion over deep data remains unsupported.
    /// </summary>
    public long EvaluationStackBudgetBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Sandbox limit: the wall-clock execution timeout applied to a single script evaluation via
    /// a Jint timeout constraint. A pathological script (e.g. an infinite loop) is aborted once this
    /// elapses instead of hanging the executing thread. <c>null</c> disables the timeout constraint.
    /// Defaults to a generous 5 seconds — long enough for legitimate scripts, short enough to bound a
    /// runaway one.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sandbox limit: the maximum number of statements a single evaluation may execute before a
    /// Jint constraint aborts it. Guards against runaway loops even when a timeout is not reached first.
    /// <c>null</c> or a non-positive value disables the statement-count constraint. Default 10,000,000.
    /// </summary>
    public int? MaxStatements { get; set; } = 10_000_000;

    /// <summary>
    /// Sandbox limit: the maximum call-stack recursion depth for a single evaluation. Guards
    /// against unbounded recursion blowing the stack. <c>null</c> or a non-positive value disables the
    /// recursion constraint. Default 300.
    /// </summary>
    /// <remarks>
    /// Every call on the stack counts, whichever function it is (issue #4901). Whatever this is set to, an evaluation
    /// also fails with a JavaScript <c>RangeError</c> ("Maximum call stack size exceeded") once it uses more stack than
    /// its evaluation thread sets aside for running, so deep calls cannot overflow the host's stack. Recursion inside
    /// Jint's own native code over deeply nested data (a very deep array or Proxy chain) is not bounded by either.
    /// </remarks>
    public int? MaxRecursionDepth { get; set; } = 300;

    /// <summary>Maximum managed memory, in bytes, that Jint may allocate during one evaluation.</summary>
    public long? MaxMemoryBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Maximum length of a JavaScript array, including sparse arrays.</summary>
    public uint? MaxArrayLength { get; set; } = 100_000;

    /// <summary>Maximum aggregate UTF-8 size of declared JSON parameter values.</summary>
    public int MaxInputBytes { get; set; } = 1024 * 1024;

    /// <summary>Maximum nesting depth of a declared JSON parameter value.</summary>
    public int MaxInputDepth { get; set; } = 64;

    /// <summary>Maximum aggregate number of JSON values across declared parameters.</summary>
    public int MaxInputNodes { get; set; } = 100_000;

    /// <summary>Maximum UTF-8 size of a materialized JSON result.</summary>
    public int MaxResultBytes { get; set; } = 1024 * 1024;

    /// <summary>Maximum nesting depth of a materialized JSON result.</summary>
    public int MaxResultDepth { get; set; } = 64;

    /// <summary>Maximum number of JSON values in a materialized result.</summary>
    public int MaxResultNodes { get; set; } = 100_000;
}
