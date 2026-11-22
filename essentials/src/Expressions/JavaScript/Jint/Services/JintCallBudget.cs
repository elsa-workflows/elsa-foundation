using System.Linq.Expressions;
using System.Reflection;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Jint 4.9.1 has no public check before function parameter initialization. Its call stack does, however, hash a
/// statistics key immediately after pushing a call, before running any user code. Check the budget there too.
/// Kept in the Jint adapter: field-shape changes fail engine construction. The crash-isolated tests also guard the
/// required push/hash/pop ordering on Jint upgrades.
/// </summary>
internal static class JintCallBudget
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type CallStackType = typeof(Engine).GetField("CallStack", InstanceFields)?.FieldType
        ?? throw new InvalidOperationException("Jint's call stack field was not found.");
    private static readonly FieldInfo Statistics = CallStackType.GetField("_statistics", InstanceFields)
        ?? throw new InvalidOperationException("Jint's call statistics field was not found.");
    private static readonly MethodInfo CreateDictionary = typeof(JintCallBudget)
        .GetMethod(nameof(Create), BindingFlags.Static | BindingFlags.NonPublic)!
        .MakeGenericMethod(Statistics.FieldType.GetGenericArguments()[0]);
    private static readonly Action<object> DiscardPendingCall = CompileDiscard();

    public static void Install(object callStack, JintEvaluationThreads.StackBudgetConstraint budget, Func<object, int> count)
    {
        var dictionary = Statistics.GetValue(callStack)
            ?? throw new InvalidOperationException("Jint's call statistics must be enabled.");
        Statistics.SetValue(callStack, CreateDictionary.Invoke(null, [dictionary, callStack, budget, count]));
    }

    private static object Create<T>(object dictionary, object callStack,
        JintEvaluationThreads.StackBudgetConstraint budget, Func<object, int> count) where T : notnull
    {
        var original = (Dictionary<T, int>)dictionary;
        if (original.Count != 0 || count(callStack) != 0)
            throw new InvalidOperationException("The Jint call budget must be installed before any calls.");

        return new Dictionary<T, int>(new GuardedComparer<T>(original.Comparer, callStack, budget, count));
    }

    private static Action<object> CompileDiscard()
    {
        var stackField = CallStackType.GetField("_stack", InstanceFields)
            ?? throw new InvalidOperationException("Jint's call stack storage was not found.");
        var pop = stackField.FieldType.GetMethod("PopAndDiscard", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Jint's pending-call rollback was not found.");
        var instance = Expression.Parameter(typeof(object));
        return Expression.Lambda<Action<object>>(Expression.Call(
            Expression.Field(Expression.Convert(instance, CallStackType), stackField), pop), instance).Compile();
    }

    private sealed class GuardedComparer<T>(IEqualityComparer<T> original, object callStack,
        JintEvaluationThreads.StackBudgetConstraint budget, Func<object, int> count)
        : IEqualityComparer<T> where T : notnull
    {
        private int _observedDepth;

        public bool Equals(T? x, T? y) => original.Equals(x, y);

        public int GetHashCode(T value)
        {
            var depth = count(callStack);
            var entering = depth > _observedDepth;
            _observedDepth = depth;
            if (entering)
            {
                try
                {
                    budget.Check();
                }
                catch
                {
                    // Push has added the pending frame, but has not updated statistics or entered the callee's
                    // try/finally yet. Roll back ONLY that frame; a normal Pop would corrupt the caller's statistics.
                    DiscardPendingCall(callStack);
                    _observedDepth--;
                    throw;
                }
            }
            // Pop hashes AFTER removing its frame. Never check on exit: it must be able to unwind a failed call.
            return original.GetHashCode(value);
        }
    }
}
