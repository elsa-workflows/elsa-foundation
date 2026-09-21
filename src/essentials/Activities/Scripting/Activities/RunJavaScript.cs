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
/// <b>Outcomes (#1114).</b> Authoring <see cref="PossibleOutcomes"/> turns the activity into a branch point: each
/// listed name becomes an outcome port, and the script <em>routes by returning</em> — its return value, read as a
/// string, selects the matching port, or <c>Unmatched</c> when it names none of them. This is the same mechanism
/// <c>SendHttpRequest.ExpectedStatusCodes</c> uses, and it is deliberately chosen over Elsa 3's host-injected
/// <c>setOutcome()</c>: the sandbox stays closed and side-effect-free, the routing decision stays a pure function
/// of the script's value, and the ports remain statically inspectable at publish time.
/// </para>
/// <para>
/// Elsa 3's <c>setOutcomes()</c> (plural) has no counterpart — an Elsa 4 completion carries exactly one outcome.
/// <see cref="RunJavaScriptResult.Value"/> is populated in every case, including <c>Unmatched</c>.
/// </para>
/// </remarks>
[ActivityValueOutcomes(RunJavaScript.PossibleOutcomesInputKey, UnmatchedOutcome = RunJavaScript.UnmatchedOutcome)]
public sealed class RunJavaScript(IJavaScriptScriptEvaluator evaluator) : Activity<RunJavaScriptResult>
{
    /// <summary>The outcome taken when <see cref="PossibleOutcomes"/> is authored but the script names none of them.</summary>
    public const string UnmatchedOutcome = "Unmatched";

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
    /// The outcome names the script may route to. Each authored name becomes an outcome port on this node; leaving
    /// it empty keeps the single implicit <c>Done</c> completion.
    /// </summary>
    [ActivityInput(Key = PossibleOutcomesInputKey)]
    public ICollection<string>? PossibleOutcomes { get; set; }

    protected override async ValueTask<ActivityTransition<RunJavaScriptResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var result = string.IsNullOrWhiteSpace(Script)
            ? null
            : await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                Script,
                Arguments,
                context.CancellationToken));
        var completion = new RunJavaScriptResult(result);

        // With no authored ports there is nothing to route on, so the activity keeps Complete's own default
        // outcome rather than restating it here.
        return PossibleOutcomes is { Count: > 0 } outcomes
            ? ActivityTransition.Complete(completion, SelectOutcome(result, outcomes))
            : ActivityTransition.Complete(completion);
    }

    /// <summary>
    /// Maps the script's return value onto one of the authored ports. Only a JSON string or number can name a port —
    /// the same restriction the publish compiler applies when it pins the ports from the authored list, and it reads
    /// a number the same way (raw text), so the two always agree. Anything else — an object, an array, null, or a
    /// name not in the list — is <see cref="UnmatchedOutcome"/>. The comparison is ordinal: an outcome port is an
    /// identifier, not display text.
    /// </summary>
    private static string SelectOutcome(JsonElement? result, ICollection<string> outcomes)
    {
        var selected = result switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString(),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null
        };

        return selected is not null && outcomes.Contains(selected, StringComparer.Ordinal)
            ? selected
            : UnmatchedOutcome;
    }
}

/// <summary>The atomic, persistable result returned by a JavaScript activity.</summary>
public sealed record RunJavaScriptResult
{
    public RunJavaScriptResult(JsonElement? value) => Value = value?.Clone();

    [Output(Key = "result", Path = "value", IsRequired = false)]
    public JsonElement? Value { get; }
}
