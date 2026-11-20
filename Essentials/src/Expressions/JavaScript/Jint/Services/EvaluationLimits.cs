using System.Diagnostics;
using Jint;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The timeout, statement and memory limits of a script, counted across the whole script rather than per call into the
/// engine. Jint's own limits restart whenever .NET code calls back into the engine (<c>Engine.Invoke</c> resets every
/// constraint), and a host function does exactly that to convert its arguments and to read variables: a script looping
/// over such calls would otherwise never reach its limits. So Jint's resets change nothing here.
/// <list type="bullet">
/// <item>The evaluator starts the count right before the script runs and again before its result and variables are
/// written, so the engine setup, the input conversion and the parse before them never count.</item>
/// <item>The engine's own work in between — a host-function call, the value bridge serializing or comparing a value — is
/// an excluded region: the statements and memory it uses do not count against the script, and inside it they are limited
/// per region, as Jint's per-call reset limited them (a script's toJSON met there stays bounded). The timeout is the
/// evaluation's wall clock and counts everything, a slow host function included.</item>
/// </list>
/// The exceptions are the ones Jint throws for the same limits; a timeout is marked so a host function's own timeout stays
/// its own error.
/// </summary>
internal sealed class EvaluationLimits(TimeSpan? timeout, int? maxStatements, long? maxMemoryBytes) : Constraint
{
    private const string LimitMark = "Elsa.Expressions.JavaScript.EvaluationLimit";

    // In Stopwatch ticks; a double, so a timeout too long to add to the clock (TimeSpan.MaxValue) never comes.
    private readonly double? _timeoutTicks = timeout?.TotalSeconds * Stopwatch.Frequency;

    private bool _started;
    private long _phaseStarted;

    // The script's own count: statements, and bytes allocated outside excluded regions.
    private long _statements;
    private long _memoryBaseline;
    private long _excludedBytes;

    // The current excluded region, counted on its own.
    private int _regionDepth;
    private long _regionStatements;
    private long _regionBaseline;

    /// <summary>Starts the count for one phase of the evaluation: the script, or writing what it left.</summary>
    public static void Start(Engine engine) => engine.Constraints.Find<EvaluationLimits>()?.Start();

    /// <summary>
    /// Marks the engine's own work (a host-function call, the value bridge) as not the script's: its statements and memory
    /// are limited on their own and do not count against the script. Dispose to end the region.
    /// </summary>
    public static Region Exclude(Engine engine) => new(engine.Constraints.Find<EvaluationLimits>());

    /// <summary>Whether an exception is a limit this constraint raised.</summary>
    public static bool IsLimit(Exception exception) => exception.Data.Contains(LimitMark);

    public override void Check()
    {
        if (!_started)
            return;
        var inRegion = _regionDepth > 0;
        if (maxStatements is { } max && (inRegion ? ++_regionStatements : ++_statements) > max)
            throw new StatementsCountOverflowException();
        if (_timeoutTicks is { } limitTicks && Stopwatch.GetTimestamp() - _phaseStarted > limitTicks)
        {
            var exception = new TimeoutException();
            exception.Data[LimitMark] = true;
            throw exception;
        }

        if (maxMemoryBytes is { } limit)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var used = inRegion ? allocated - _regionBaseline : allocated - _memoryBaseline - _excludedBytes;
            if (used > limit)
                throw new MemoryLimitExceededException($"Script has allocated {used} but is limited to {limit}");
        }
    }

    // Jint resets on every call into the engine, including a host function's: the count is started by the evaluator.
    public override void Reset()
    {
    }

    private void Start()
    {
        _started = true;
        _phaseStarted = Stopwatch.GetTimestamp();
        _statements = 0;
        _excludedBytes = 0;
        _memoryBaseline = GC.GetAllocatedBytesForCurrentThread();
    }

    private void Enter()
    {
        if (_regionDepth++ > 0)
            return;
        _regionStatements = 0;
        _regionBaseline = GC.GetAllocatedBytesForCurrentThread();
    }

    private void Exit()
    {
        if (--_regionDepth > 0)
            return;
        _excludedBytes += GC.GetAllocatedBytesForCurrentThread() - _regionBaseline;
    }

    /// <summary>An excluded region; a no-op when the engine has no limits.</summary>
    public readonly struct Region : IDisposable
    {
        private readonly EvaluationLimits? _limits;

        internal Region(EvaluationLimits? limits)
        {
            _limits = limits;
            _limits?.Enter();
        }

        public void Dispose() => _limits?.Exit();
    }
}
