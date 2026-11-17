using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Activities.Scripting.Activities;

/// <summary>
/// Executes a stateful JavaScript program from explicit pinned inputs and returns one typed result.
/// </summary>
/// <remarks>
/// <para>
/// The script receives only <c>args</c>, a frozen JSON object. It cannot read or write workflow variables,
/// inputs, outputs, services, configuration, or the ambient activity context. A workflow assignment is a
/// separate graph-visible <c>Set</c> intrinsic that consumes this activity's result.
/// </para>
/// <para>
/// Constructor parameters are services; workflow data is hydrated into the ordinary properties below before
/// execution. The evaluator applies its configured timeout, statement, recursion, and cancellation limits.
/// </para>
/// <para>
/// <b>Outcomes (ADR 0045 §6, spec 1208).</b> The script selects outcomes only by returning the value of the built-in
/// pure constructor <c>complete(result, outcomes)</c>: <c>return complete(order, 'Approved')</c> selects one outcome,
/// <c>return complete(order, ['Approved', 'Audit'])</c> several, <c>return complete(order, [])</c> none, and
/// <c>return complete(order)</c> or any ordinary returned value completes with <c>Done</c>. A returned value is the
/// activity's result only and never selects an outcome, so returning data cannot change the route (#1114 routed by
/// return value; that is gone, and so is its <c>Unmatched</c> outcome).
/// </para>
/// <para>
/// Authoring <see cref="PossibleOutcomes"/> declares the node's outcome set: <c>Done</c> plus each listed name, pinned
/// at publication. A completion may select several of them. A selected name outside the set, or an invalid
/// <c>complete</c> value, faults the step with <c>VF-ACT-006</c>. The selection takes effect only when the script
/// completes successfully; a fault discards it.
/// </para>
/// </remarks>
[ActivityValueOutcomes(RunJavaScript.PossibleOutcomesInputKey, UnmatchedOutcome = null, SelectsOnEveryCompletion = false)]
[ActivityScriptOutcomes("script")]
public sealed class RunJavaScript(IJavaScriptScriptEvaluator evaluator) : Activity<RunJavaScriptResult>
{
    /// <summary>
    /// The contract key of <see cref="PossibleOutcomes"/>. <c>ActivityValueOutcomes</c> resolves its source by
    /// contract key (that is how the publish compiler looks up the authored binding), not by property name, so the
    /// two must be spelled from the same constant.
    /// </summary>
    public const string PossibleOutcomesInputKey = "possibleOutcomes";

    /// <summary>The stable activity type key, resolved by the runtime's CLR activity constructor.</summary>
    public const string ActivityType = "Elsa.RunJavaScript";

    /// <summary>The JavaScript program to execute.</summary>
    [ActivityInput(Key = "script")]
    public required string Script { get; set; }

    /// <summary>The complete explicit argument object exposed to the program as read-only <c>args</c>.</summary>
    [ActivityInput(Key = "arguments", DefaultValue = "{}")]
    public JsonElement Arguments { get; set; } = JsonSerializer.SerializeToElement(new { });

    /// <summary>
    /// The outcome names the script may select with <c>complete(result, outcomes)</c>. Each authored name becomes an
    /// outcome port on this node, next to <c>Done</c>.
    /// </summary>
    [ActivityInput(Key = PossibleOutcomesInputKey)]
    public ICollection<string>? PossibleOutcomes { get; set; }

    protected override async ValueTask<ActivityTransition<RunJavaScriptResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(Script))
            return ActivityTransition.Complete(new RunJavaScriptResult(null));

        JavaScriptScriptEvaluationResult evaluation;
        try
        {
            evaluation = await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                Script,
                Arguments,
                context.CancellationToken));
        }
        catch (JavaScriptCompletionException exception)
        {
            return ActivityTransition.Fault<RunJavaScriptResult>(new ActivityFault(exception.Code, exception.Message));
        }

        // The selection travels next to the result; whether its names are in this node's pinned outcome set is checked
        // when the completion is projected, as for every other activity.
        var selection = evaluation.SelectionState switch
        {
            JavaScriptOutcomeSelectionState.Named => OutcomeSelection.Named(evaluation.OutcomeNames),
            JavaScriptOutcomeSelectionState.Empty => OutcomeSelection.Empty,
            _ => OutcomeSelection.Unspecified
        };
        return ActivityTransition.Complete(new RunJavaScriptResult(evaluation.Value), selection);
    }
}

/// <summary>The atomic, persistable result returned by a JavaScript activity.</summary>
public sealed record RunJavaScriptResult
{
    public RunJavaScriptResult(JsonElement? value) => Value = value?.Clone();

    [Output(Key = "result", Path = "value", IsRequired = false)]
    public JsonElement? Value { get; }
}
