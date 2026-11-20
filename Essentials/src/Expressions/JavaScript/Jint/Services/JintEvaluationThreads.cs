using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Jint;
using Jint.Native.Error;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Runs every Jint evaluation on a dedicated thread with a large stack, so a script cannot exhaust the stack and take
/// the host process down (issue #4901).
/// </summary>
/// <remarks>
/// <para>
/// When one of Jint's own limits fires (recursion depth, timeout, statement count, memory, cancellation), Jint throws a
/// .NET exception that every enclosing JavaScript statement list catches and throws again from inside its catch
/// handler. Each of those nested throws keeps its exception-dispatch frames on the stack until the outermost handler
/// runs: about 7.5 KB per enclosing statement list, against about 0.7 KB to 1.4 KB that the same statement list used
/// while running. A script that is 200 blocks or recursive calls deep when a limit fires therefore needs about 1.5 MB
/// just to unwind, more than a 1 MB or 1.5 MB thread-pool thread has, whatever the configured recursion limit.
/// </para>
/// <para>
/// Two measures bound that cost together. The script may use at most <see cref="StackBudget"/> bytes of stack while
/// running; past that, the next statement fails with a JavaScript <c>RangeError</c>, which Jint unwinds as an
/// ordinary script error (no nested throws). The evaluation thread has <see cref="StackSize"/> bytes, enough to unwind
/// a limit exception from anywhere within that budget with room to spare. Threads are reused across evaluations and retire
/// after <see cref="IdleTimeout"/> idle; an evaluation started from inside another (on an evaluation thread) runs
/// inline and shares the outer evaluation's budget.
/// </para>
/// </remarks>
internal static class JintEvaluationThreads
{
    /// <summary>The stack size of an evaluation thread. Only touched pages are committed.</summary>
    internal const int StackSize = 16 * 1024 * 1024;

    /// <summary>The stack an evaluation may use while running before it fails with a <c>RangeError</c>.</summary>
    internal const long StackBudget = 512 * 1024;

    internal const string StackBudgetExceededMessage = "Maximum call stack size exceeded";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static readonly List<Worker> IdleWorkers = [];

    [ThreadStatic] private static Worker? _current;

    /// <summary>Runs <paramref name="work"/> on an evaluation thread and returns its result or rethrows its exception.</summary>
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_current is not null)
            return work();

        var item = new WorkItem<T>(work, ExecutionContext.Capture());
        Rent().Execute(item);
        item.Wait();
        return item.Result();
    }

    /// <summary>
    /// The stack the current evaluation has used since it started on its evaluation thread; zero on any other thread,
    /// where no budget applies.
    /// </summary>
    internal static long UsedStack() => _current is { } worker ? worker.Origin - StackPosition() : 0;

    /// <summary>Whether the current thread is an evaluation thread.</summary>
    internal static bool IsEvaluationThread => _current is not null;

    // The address of a local, as a number: the stack grows down on every platform .NET supports.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long StackPosition()
    {
        byte marker = 0;
        return Unsafe.ByteOffset(ref Unsafe.NullRef<byte>(), ref marker);
    }

    private static Worker Rent()
    {
        lock (Gate)
        {
            if (IdleWorkers.Count > 0)
            {
                var worker = IdleWorkers[^1];
                IdleWorkers.RemoveAt(IdleWorkers.Count - 1);
                return worker;
            }
        }

        return new Worker();
    }

    private static void Return(Worker worker)
    {
        lock (Gate)
            IdleWorkers.Add(worker);
    }

    // Retires an idle worker; false when a caller has just rented it, so a work item is on its way.
    private static bool TryRetire(Worker worker)
    {
        lock (Gate)
            return IdleWorkers.Remove(worker);
    }

    /// <summary>
    /// Fails the next statement with a JavaScript <c>RangeError</c> once the evaluation is over its stack budget. It
    /// checks nothing until it is attached to its engine, and nothing off an evaluation thread.
    /// </summary>
    internal sealed class StackBudgetConstraint : Constraint
    {
        private ErrorConstructor? _rangeError;

        /// <summary>Attaches the engine before any script runs, so a script cannot replace the error it throws.</summary>
        public void Attach(Engine engine) => _rangeError = (ErrorConstructor)engine.Global.Get("RangeError");

        public override void Check()
        {
            if (_rangeError is { } rangeError && UsedStack() > StackBudget)
                throw new JavaScriptException(rangeError, StackBudgetExceededMessage);
        }

        public override void Reset()
        {
        }
    }

    private abstract class WorkItem
    {
        public abstract void Execute();

        public abstract void Complete();
    }

    private sealed class WorkItem<T>(Func<T> work, ExecutionContext? context) : WorkItem
    {
        // Spinning first keeps the hand-off near the cost of running inline for the usual short evaluation.
        private readonly ManualResetEventSlim _done = new(false, 1000);
        private Func<T>? _work = work;
        private ExecutionContext? _context = context;
        private T _result = default!;
        private ExceptionDispatchInfo? _error;

        public override void Execute()
        {
            try
            {
                if (_context is null)
                    _result = _work!();
                else
                    ExecutionContext.Run(_context, static state => ((WorkItem<T>)state!).RunWork(), this);
            }
            catch (Exception exception)
            {
                _error = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                // The evaluation thread outlives this item: it must not keep the script or its caller's context alive.
                _work = null;
                _context = null;
            }
        }

        public override void Complete() => _done.Set();

        private void RunWork() => _result = _work!();

        public void Wait() => _done.Wait();

        public T Result()
        {
            _error?.Throw();
            var result = _result;
            _result = default!;
            return result;
        }
    }

    private sealed class Worker
    {
        private readonly SemaphoreSlim _signal = new(0);
        private WorkItem? _item;

        public Worker()
        {
            var thread = new Thread(Loop, StackSize) { IsBackground = true, Name = "Elsa JavaScript evaluation" };
            // The thread outlives the caller that created it: it must not keep that caller's execution context.
            thread.UnsafeStart();
        }

        public long Origin { get; private set; }

        public void Execute(WorkItem item)
        {
            _item = item;
            _signal.Release();
        }

        private void Loop()
        {
            _current = this;
            while (true)
            {
                if (!_signal.Wait(IdleTimeout))
                {
                    if (TryRetire(this))
                        return;
                    continue;
                }

                RunNext();
            }
        }

        // A separate frame, so nothing of the item stays reachable from this thread's stack while it is idle.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RunNext()
        {
            var item = _item!;
            _item = null;
            Origin = StackPosition();
            item.Execute();
            // Idle again before the caller wakes, so its next evaluation reuses this thread instead of starting one.
            Return(this);
            item.Complete();
        }
    }
}
