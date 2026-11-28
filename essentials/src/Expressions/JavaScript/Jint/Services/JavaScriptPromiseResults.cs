using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Issue #5687: a script or input expression whose result is a Promise yields the value the Promise settled with, as
/// Elsa 3 did (its evaluator unwrapped the result with Jint's <c>UnwrapIfPromise</c>). Without this the value bridge
/// writes the Promise object itself, which has no own members, so the result silently became <c>{}</c>.
/// </summary>
/// <remarks>
/// Only the top-level result is unwrapped, as in Elsa 3; a Promise nested in returned data stays an empty object there
/// as it did in Elsa 3. A rejected Promise faults the evaluation the way <c>throw reason</c> would. A Promise that has
/// not settled once the script and its queued continuations have run never will (an evaluation has no timers or other
/// event sources), so it faults at once with <see cref="PendingPromiseResultException"/>; Elsa 3 faulted too, after
/// waiting for Jint's promise timeout.
/// </remarks>
internal static class JavaScriptPromiseResults
{
    public static JsValue Settle(Engine engine, JsValue value)
    {
        if (!value.IsPromise())
            return value;

        // Continuations the script queued run as part of its own work, under the same limits. After this, reading the
        // Promise runs nothing more: the zero wait below only reports its state.
        engine.Advanced.ProcessTasks();
        try
        {
            return value.UnwrapIfPromise(TimeSpan.Zero);
        }
        catch (PromiseRejectedException rejected)
        {
            throw new JavaScriptException(rejected.RejectedValue);
        }
        catch (InvalidOperationException)
        {
            // Jint reports a Promise that is still pending this way.
            throw new PendingPromiseResultException();
        }
    }
}

/// <summary>A pending Promise has no value or path to redact, so its recovery guidance stays visible.</summary>
internal sealed class PendingPromiseResultException : InvalidOperationException, Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
{
    private const string Description = "VF-PROMISE-001: The JavaScript result is a Promise that never settles. " +
        "Resolve or reject the Promise within the script (an evaluation has no timers or other event sources), or return the value itself instead of a Promise.";

    public PendingPromiseResultException() : base(Description) { }

    public string ValueFreeMessage => Description;
}
