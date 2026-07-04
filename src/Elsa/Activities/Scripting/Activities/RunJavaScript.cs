using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Activities.Scripting.Activities;

/// <summary>
/// Executes an authored JavaScript <see cref="Script"/> and surfaces its return value as the activity result
/// (W16). The script runs through the shared <see cref="IJavaScriptEvaluator"/> — the same Jint-backed pipeline
/// used for JavaScript expressions — so it reuses the workflow variable/input/output accessors and the engine
/// caching the expression path already provides, rather than forking a second scripting stack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sandbox (DS-9).</b> The evaluation is bounded by the shared Jint sandbox: the caller's
/// <see cref="IActivityExecutionContext.CancellationToken"/> is threaded into the engine (a cancelled token
/// aborts the running script), and the engine enforces the configured wall-clock timeout, statement-count, and
/// recursion-depth limits (see the Jint engine's feature options). Those constraints are applied centrally by
/// the engine factory, so every script — activity or expression — is bounded by the same policy object; the
/// remaining DS-9 hardening for non-activity evaluator entry points is tracked as a separate follow-up.
/// </para>
/// <para>
/// <b>Result.</b> The script's completion value becomes <see cref="CodeActivityWithResult.Result"/> when it is
/// non-null; a null result (or a whitespace-only script) leaves the result unset. The activity auto-completes
/// with the default outcome — the richer <c>setOutcome</c>/<c>setOutcomes</c> scripting surface is intentionally
/// out of this increment and tracked as a follow-up.
/// </para>
/// </remarks>
public sealed class RunJavaScript : CodeActivity<object?>
{
    /// <summary>The stable activity type key, resolved by the runtime's CLR activity constructor.</summary>
    public const string ActivityType = "Elsa.RunJavaScript";

    public RunJavaScript() : base(ActivityType)
    {
    }

    /// <summary>The JavaScript source to execute. Its return value becomes the activity result.</summary>
    public InputArgument<string> Script { get; set; } = null!;

    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        var script = context.Get(Script);

        // No script authored: nothing to evaluate, auto-complete with no result.
        if (string.IsNullOrWhiteSpace(script))
            return;

        var evaluator = context.GetRequiredService<IJavaScriptEvaluator>();

        // Thread the activity's cancellation token so a cancelled run aborts the script (DS-9); the shared engine
        // factory additionally enforces the configured timeout/statement/recursion sandbox limits.
        var result = await evaluator.EvaluateAsync(
            script,
            typeof(object),
            context.ExpressionExecutionContext,
            cancellationToken: context.CancellationToken);

        if (result is not null)
            context.Set(Result, result);
    }
}
