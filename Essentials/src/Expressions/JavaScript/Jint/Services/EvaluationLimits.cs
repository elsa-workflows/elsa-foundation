using System.Diagnostics;
using Jint;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The timeout, statement and memory limits of a script, counted across the whole script rather than per call into the
/// engine. Jint's own limits restart whenever .NET code calls back into the engine (<c>Engine.Invoke</c> resets every
/// constraint), and a host function does exactly that to convert its arguments and to read variables: a script looping
/// over such calls would otherwise never reach its limits. So Jint's resets change nothing here; the evaluator starts the
/// count right before the script runs and again before its result and variables are written, the two phases Jint's
/// limits covered, so the engine setup and the input conversion before them never count. The exceptions are the ones
/// Jint throws for the same limits; a timeout is marked so a host function's own timeout stays its own error.
/// </summary>
internal sealed class EvaluationLimits(TimeSpan? timeout, int? maxStatements, long? maxMemoryBytes) : Constraint
{
    private const string LimitMark = "Elsa.Expressions.JavaScript.EvaluationLimit";

    private bool _started;
    private long _deadline;
    private long _statements;
    private long _memoryBaseline;

    /// <summary>Starts the count for one phase of the evaluation: the script, or writing what it left.</summary>
    public static void Start(Engine engine) => engine.Constraints.Find<EvaluationLimits>()?.Start();

    /// <summary>Whether an exception is a limit this constraint raised.</summary>
    public static bool IsLimit(Exception exception) => exception.Data.Contains(LimitMark);

    public override void Check()
    {
        if (!_started)
            return;
        if (maxStatements is { } max && ++_statements > max)
            throw new StatementsCountOverflowException();
        if (timeout is not null && Stopwatch.GetTimestamp() > _deadline)
        {
            var exception = new TimeoutException();
            exception.Data[LimitMark] = true;
            throw exception;
        }

        if (maxMemoryBytes is { } limit && GC.GetAllocatedBytesForCurrentThread() - _memoryBaseline > limit)
            throw new MemoryLimitExceededException($"Script has allocated {GC.GetAllocatedBytesForCurrentThread() - _memoryBaseline} but is limited to {limit}");
    }

    // Jint resets on every call into the engine, including a host function's: the count is started by the evaluator.
    public override void Reset()
    {
    }

    private void Start()
    {
        _started = true;
        _statements = 0;
        _memoryBaseline = GC.GetAllocatedBytesForCurrentThread();
        if (timeout is { } interval)
        {
            // A timeout too long to add to the clock never comes (TimeSpan.MaxValue is a common "no limit").
            var now = Stopwatch.GetTimestamp();
            var ticks = interval.TotalSeconds * Stopwatch.Frequency;
            _deadline = ticks >= long.MaxValue - now ? long.MaxValue : now + (long)ticks;
        }
    }
}
