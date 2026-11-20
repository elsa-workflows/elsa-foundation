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
/// <item>Every statement counts, wherever it runs: a toJSON, getter or Proxy trap met while a value is converted is the
/// script's own code.</item>
/// <item>The memory the value bridge allocates in plain .NET work — parsing, comparing and patching JSON, where no script
/// code can run — does not count against the script (<see cref="ExcludeHostWork"/>). Should script code run inside such
/// a section after all, its memory counts.</item>
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
    private long _statements;
    private long _memoryBaseline;
    private long _excludedBytes;

    // The current host-work section.
    private int _hostWorkDepth;
    private long _hostWorkBaseline;
    private bool _scriptRanInHostWork;

    /// <summary>Starts the count for one phase of the evaluation: the script, or writing what it left.</summary>
    public static void Start(Engine engine) => engine.Constraints.Find<EvaluationLimits>()?.Start();

    /// <summary>
    /// Marks plain .NET work of the host (parsing, comparing, patching JSON): the memory it allocates does not count against
    /// the script, unless script code runs inside it. Dispose to end the section.
    /// </summary>
    public static HostWork ExcludeHostWork(Engine engine) => new(engine.Constraints.Find<EvaluationLimits>());

    /// <summary>Whether an exception is a limit this constraint raised.</summary>
    public static bool IsLimit(Exception exception) => exception.Data.Contains(LimitMark);

    public override void Check()
    {
        if (!_started)
            return;
        if (_hostWorkDepth > 0)
            _scriptRanInHostWork = true;
        if (maxStatements is { } max && ++_statements > max)
            throw new StatementsCountOverflowException();
        if (_timeoutTicks is { } limitTicks && Stopwatch.GetTimestamp() - _phaseStarted > limitTicks)
        {
            var exception = new TimeoutException();
            exception.Data[LimitMark] = true;
            throw exception;
        }

        if (maxMemoryBytes is { } limit)
        {
            var used = GC.GetAllocatedBytesForCurrentThread() - _memoryBaseline - _excludedBytes;
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

    private void EnterHostWork()
    {
        if (_hostWorkDepth++ > 0)
            return;
        _hostWorkBaseline = GC.GetAllocatedBytesForCurrentThread();
        _scriptRanInHostWork = false;
    }

    private void ExitHostWork()
    {
        if (--_hostWorkDepth > 0)
            return;
        if (!_scriptRanInHostWork)
            _excludedBytes += GC.GetAllocatedBytesForCurrentThread() - _hostWorkBaseline;
    }

    /// <summary>A host-work section; a no-op when the engine has no limits.</summary>
    public readonly struct HostWork : IDisposable
    {
        private readonly EvaluationLimits? _limits;

        internal HostWork(EvaluationLimits? limits)
        {
            _limits = limits;
            _limits?.EnterHostWork();
        }

        public void Dispose() => _limits?.ExitHostWork();
    }
}
