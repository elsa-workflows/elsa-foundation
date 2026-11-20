using System.Diagnostics;
using Jint;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The timeout, statement and memory limits of one evaluation, counted from its first statement to its last. Jint's own
/// limits restart whenever .NET code calls back into the engine (<c>Engine.Invoke</c> resets every constraint), and a
/// host function does exactly that to convert its arguments and to read variables: a script looping over such calls would
/// otherwise never reach its limits. Only the first reset starts the count; later ones change nothing. The exceptions are
/// the ones Jint throws for the same limits.
/// </summary>
internal sealed class EvaluationLimits(TimeSpan? timeout, int? maxStatements, long? maxMemoryBytes) : Constraint
{
    private bool _started;
    private long _deadline;
    private long _statements;
    private long _memoryBaseline;

    public override void Check()
    {
        if (maxStatements is { } max && ++_statements > max)
            throw new StatementsCountOverflowException();
        if (timeout is not null && Stopwatch.GetTimestamp() > _deadline)
            throw new TimeoutException();
        if (maxMemoryBytes is { } limit && GC.GetAllocatedBytesForCurrentThread() - _memoryBaseline > limit)
            throw new MemoryLimitExceededException($"Script has allocated more than {limit} bytes.");
    }

    public override void Reset()
    {
        if (_started)
            return;
        _started = true;
        if (timeout is { } interval)
            _deadline = Stopwatch.GetTimestamp() + (long)(interval.TotalSeconds * Stopwatch.Frequency);
        _memoryBaseline = GC.GetAllocatedBytesForCurrentThread();
    }
}
