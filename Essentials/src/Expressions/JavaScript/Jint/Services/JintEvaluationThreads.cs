using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Jint;
using Jint.Native.Error;
using Jint.Runtime;
using Elsa.Expressions.JavaScript.Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Runs every Jint evaluation on a dedicated thread with a large stack, so deep script calls fail the evaluation instead
/// of exhausting the stack and taking the host process down (issue #4901).
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
/// The measures bound that cost together:
/// <list type="bullet">
/// <item>The script may use at most <see cref="StackBudget"/> bytes of stack while running statements; past that, the
/// next statement fails with a JavaScript <c>RangeError</c>, which Jint unwinds as an ordinary script error (no nested
/// throws). The same check runs first, before the other limits, and at a host function's entry, so no limit exception
/// and no host function error is thrown deeper than the budget.</item>
/// <item>Calls are checked before parameter initialization too, through <see cref="JintCallBudget"/>. Statement-free
/// chains therefore cannot escape the budget and throw an expression-level limit near the stack's end. Jint's own
/// recursion limit is replaced by the same check on total call depth.</item>
/// <item>The evaluation thread has <see cref="StackSize"/> bytes, enough to unwind a limit exception from anywhere
/// within the budget with room to spare.</item>
/// </list>
/// Threads are reused across evaluations and retire after <see cref="IdleTimeout"/> idle; an evaluation started from
/// inside another (on an evaluation thread) runs inline and shares the outer evaluation's budget. Recursion inside
/// Jint's native code over deeply nested data (a very deep array or Proxy chain) runs no statement and no call, and is
/// not bounded here. Native dispatch that does not record a Jint call (bound functions and Proxy forwarding) can
/// likewise bypass the pre-parameter check; it is outside this adapter's expression-level safety guarantee.
/// </para>
/// <para>
/// Host settings may only tighten the 512 KiB budget or increase the 16 MiB stack, up to 64 MiB. These bounds preserve
/// the tested unwind reserve; they are not a proof of safety for arbitrary native engine code. The feature owns and
/// enforces the budget ceiling even when a host disables the optional recursion limit.
/// </para>
/// </remarks>
internal static class JintEvaluationThreads
{
    /// <summary>Minimum thread stack; hosts may reserve more, never less. Only touched pages are committed.</summary>
    internal const int StackSize = 16 * 1024 * 1024;

    /// <summary>Hard execution-budget ceiling owned by this Jint adapter; hosts may only tighten it.</summary>
    internal const long StackBudget = 512 * 1024;

    internal const string StackBudgetExceededMessage = "Maximum call stack size exceeded";

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static readonly List<Worker> IdleWorkers = [];

    [ThreadStatic] private static Worker? _current;

    /// <summary>Runs <paramref name="work"/> on an evaluation thread and returns its result or rethrows its exception.</summary>
    public static T Run<T>(Func<T> work, FeatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        options ??= new FeatureOptions();
        // Validate the captured values, so concurrent host mutation cannot change a safety bound after validation.
        var stackSize = options.EvaluationThreadStackBytes;
        var budget = options.EvaluationStackBudgetBytes;
        if (!ValidSettings(stackSize, budget))
            throw new ArgumentOutOfRangeException(nameof(options), "Jint thread stack must be 16–64 MiB and stack budget 64–512 KiB.");

        if (_current is { } current)
        {
            var used = UsedStack();
            if (current.Budget <= used)
                throw new InsufficientExecutionStackException();
            if (current.StackSize >= stackSize)
            {
                var previousBudget = current.Budget;
                current.Budget = Math.Min(previousBudget, used + budget);
                try { return work(); }
                finally { current.Budget = previousBudget; }
            }
            // A nested host asking for a larger stack gets a matching worker, but only the outer budget's remainder.
            // It cannot reset the outer budget by switching hosts or threads.
            budget = Math.Min(budget, current.Budget - used);
        }

        var item = new WorkItem<T>(work, ExecutionContext.Capture(), budget);
        Rent(stackSize).Execute(item);
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

    internal static long CurrentBudget => _current?.Budget ?? 0;
    internal static int CurrentStackSize => _current?.StackSize ?? 0;

    internal static bool ValidSettings(FeatureOptions options) =>
        ValidSettings(options.EvaluationThreadStackBytes, options.EvaluationStackBudgetBytes);

    private static bool ValidSettings(int stackSize, long budget) =>
        stackSize is >= StackSize and <= 64 * 1024 * 1024 && budget is >= 64 * 1024 and <= StackBudget;

    // The address of a local, as a number: the stack grows down on every platform .NET supports.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long StackPosition()
    {
        byte marker = 0;
        return Unsafe.ByteOffset(ref Unsafe.NullRef<byte>(), ref marker);
    }

    private static Worker Rent(int stackSize)
    {
        lock (Gate)
        {
            var index = IdleWorkers.FindLastIndex(worker => worker.StackSize == stackSize);
            if (index >= 0)
            {
                var worker = IdleWorkers[index];
                IdleWorkers.RemoveAt(index);
                return worker;
            }
        }

        return new Worker(stackSize);
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
    /// The stack and call-depth limits, checked before every statement and before any other constraint, so that no other
    /// limit fires deeper than the budget. Over the stack budget (on an evaluation thread) the statement fails with a
    /// JavaScript <c>RangeError</c>. Over <c>MaxRecursionDepth</c> calls in all, it fails with Jint's
    /// <see cref="RecursionDepthOverflowException"/>. Jint's own recursion limit is not used: it counts each function
    /// separately and is checked on every call, so a chain of calls that runs no statement (calls in default
    /// parameters) can trip it at any depth. Nothing is checked until the constraint is attached to its engine.
    /// </summary>
    internal sealed class StackBudgetConstraint(int? maxCallDepth) : Constraint
    {
        // Jint keeps its call stack internal; the depth limit needs its size. A Jint upgrade that moves it fails the
        // tests (JintStackExhaustionTests), not a host.
        private static readonly FieldInfo CallStackField =
            typeof(Engine).GetField("CallStack", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Jint's Engine.CallStack field was not found.");
        // Compiled once: this runs before every statement.
        private static readonly Func<object, int> CallStackCount = CompileCount();
        private static readonly ConstructorInfo RecursionOverflow =
            typeof(RecursionDepthOverflowException).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [CallStackField.FieldType, typeof(string)])
            ?? throw new InvalidOperationException("Jint's RecursionDepthOverflowException constructor was not found.");

        private ErrorConstructor? _rangeError;
        private object? _callStack;

        /// <summary>Attaches the engine before any script runs, so a script cannot replace the error it throws.</summary>
        public void Attach(Engine engine)
        {
            _rangeError = (ErrorConstructor)engine.Global.Get("RangeError");
            _callStack = CallStackField.GetValue(engine);
            JintCallBudget.Install(_callStack!, this, CallStackCount);
        }

        /// <summary>Checks the limits of <paramref name="engine"/> now, outside a statement (a host function's entry).</summary>
        public static void CheckNow(Engine engine) => engine.Constraints.Find<StackBudgetConstraint>()?.Check();

        public override void Check()
        {
            if (_rangeError is not { } rangeError)
                return;
            if (IsEvaluationThread && UsedStack() > CurrentBudget)
                throw new JavaScriptException(rangeError, StackBudgetExceededMessage);
            if (maxCallDepth is { } max && CallStackCount(_callStack!) > max)
                throw (Exception)RecursionOverflow.Invoke([_callStack, string.Empty]);
        }

        public override void Reset()
        {
        }

        private static Func<object, int> CompileCount()
        {
            var count = CallStackField.FieldType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Jint's call stack Count was not found.");
            var callStack = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, int>>(
                Expression.Property(Expression.Convert(callStack, CallStackField.FieldType), count), callStack).Compile();
        }
    }

    private abstract class WorkItem
    {
        public abstract long Budget { get; }
        public abstract void Execute();

        public abstract void Complete();
    }

    private sealed class WorkItem<T>(Func<T> work, ExecutionContext? context, long budget) : WorkItem
    {
        public override long Budget => budget;
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

        public Worker(int stackSize)
        {
            StackSize = stackSize;
            var thread = new Thread(Loop, stackSize) { IsBackground = true, Name = "Elsa JavaScript evaluation" };
            // The thread outlives the caller that created it: it must not keep that caller's execution context.
            thread.UnsafeStart();
        }

        public long Origin { get; private set; }
        public int StackSize { get; }
        public long Budget { get; set; }

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
            Budget = item.Budget;
            item.Execute();
            // Idle again before the caller wakes, so its next evaluation reuses this thread instead of starting one.
            Return(this);
            item.Complete();
        }
    }
}
