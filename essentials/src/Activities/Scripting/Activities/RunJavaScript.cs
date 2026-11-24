using System.Text.Json.Serialization;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Scripting.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;

namespace Elsa.Activities.Scripting.Activities;

/// <summary>
/// Executes a stateful JavaScript program from explicit pinned inputs and returns one typed result.
/// </summary>
/// <remarks>
/// <para>
/// The script receives <c>args</c>, plain JavaScript values built from JSON; changing them is local to the run
/// (spec 1228). It reads and writes workflow variables through <c>variables</c> (<c>variables.order</c>,
/// <c>variables['order']</c>), <c>getVariable(name)</c> and <c>setVariable(name, value)</c>, over the step's variable
/// session (spec 1222 FR-033, spec 1231, ADR 0090):
/// a read returns the committed value plus the step's own changes and is recorded; a change it makes, by assigning the
/// variable or a member of a value it read, is written with the step, and a change made without assigning the
/// variable is written too, with a warning (ADR 0090 C2). The body also receives the bounded
/// <c>workflowState</c> capability for explicit workflow output and identity assignments (spec 1234). It cannot reach
/// services, configuration or the ambient activity context.
/// </para>
/// <para>
/// <c>workflowState.getOutput/setOutput</c>, <c>getCorrelationId/setCorrelationId</c> and
/// <c>getInstanceName/setInstanceName</c> read pending state and stage changes in this evaluation's checkpoint.
/// Output reads are JSON copies; setters return undefined. Faults, cancellation and caught host refusals discard the
/// evaluation's successful effects. Existing waits retain their captured correlation keys. This capability is unavailable
/// to input expressions, background workers and closed reusable graph bodies.
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
// Deliberately not [BackgroundExecutionSafe] (ADR 0093 C9, spec 1230): a script reads variables by name (ADR 0089) and
// writes them (ADR 0090 decision 3), and a background worker has no access to variables. Marking it needs that access first.
[ActivityValueOutcomes(RunJavaScript.PossibleOutcomesInputKey, UnmatchedOutcome = null, SelectsOnEveryCompletion = false)]
[ActivityScriptOutcomes("script")]
public sealed class RunJavaScript(IJavaScriptScriptEvaluator evaluator) : Activity<RunJavaScriptResult>, IStepVariableSessionConsumer, IStepWorkflowStateConsumer
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

        // A step body has a variable session; a context built by hand (outside a step) has none, and the script then has
        // no `variables` root.
        IStepVariableSession? session;
        try
        {
            session = context.Variables as IStepVariableSession;
        }
        catch (InvalidOperationException)
        {
            session = null;
        }

        var variables = session is null ? null : new StepScriptVariables(session, context.HasProtectedInputs);
        StepScriptWorkflowState? workflowState = null;
        try
        {
            workflowState = new StepScriptWorkflowState(context.WorkflowState, () => context.HasProtectedInputs || session?.HasReadProtectedValue == true);
        }
        catch (InvalidOperationException) { }
        JavaScriptScriptEvaluationResult evaluation;
        try
        {
            evaluation = await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                Script,
                Arguments,
                context.CancellationToken)
            {
                Warnings = context.Warnings,
                UndefinedAsNull = context.UndefinedAsNull,
                Variables = variables,
                WorkflowState = workflowState
            });
        }
        catch (JavaScriptCompletionException exception)
        {
            workflowState?.ThrowIfRefused();
            ThrowIfRefused(variables);
            return ActivityTransition.Fault<RunJavaScriptResult>(new ActivityFault(exception.Code, exception.Message));
        }
        catch (JavaScriptPrecisionException exception)
        {
            workflowState?.ThrowIfRefused();
            ThrowIfRefused(variables);
            // Spec 1228 FR-013: the precision policy refused the result; the step faults and nothing of it is written. With
            // a protected input the fault names no number and no path from it (spec 1229 FR-006).
            return ActivityTransition.Fault<RunJavaScriptResult>(new ActivityFault(exception.Code,
                context.HasProtectedInputs || session?.HasReadProtectedValue == true || workflowState?.HasReadProtectedValue == true ? exception.ValueFreeMessage : exception.Message));
        }
        catch (ScriptVariableWriteRefusedException)
        {
            workflowState?.ThrowIfRefused();
            ThrowIfRefused(variables);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A read the session refused is the cause, even when the script caught the error and failed otherwise
            // (FR-017a); its message is the session's own and names no value.
            workflowState?.ThrowIfRefused();
            ThrowIfRefused(variables);
            // Spec 1229 FR-006: a script error's text can quote what the script read (`throw new Error(variables.ssn)`,
            // a JSON.parse error). With a protected input or read it is not recorded; the type still says what failed. A
            // script that does not parse (Jint's ScriptPreparationException) has read nothing, but its text can quote the
            // script, which a protected input can supply, so it keeps its text only when no input is protected.
            var isParseError = exception.GetType().Name is "ScriptPreparationException" or "ParseErrorException";
            if (context.HasProtectedInputs || ((session?.HasReadProtectedValue == true || workflowState?.HasReadProtectedValue == true) && !isParseError))
            {
                if (exception is Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure valueFree)
                    throw new RedactedScriptFailureException(valueFree.ValueFreeMessage);
                throw new InvalidOperationException($"The script failed ({exception.GetType().Name}). Its message is withheld because a value it read is protected.");
            }
            throw;
        }

        workflowState?.ThrowIfRefused();

        // Spec 1222 FR-011: a name no visible variable has read undefined; the warning names it unless a value the script
        // read is protected, since a computed name can come from that value.
        if (variables is not null)
        {
            foreach (var warning in variables.MissingNameWarnings())
                context.Warnings?.Report(session!.HasReadProtectedValue ? warning.ProtectedForm ?? warning : warning);
        }

        // The selection travels next to the result; whether its names are in this node's pinned outcome set is checked
        // when the completion is projected, as for every other activity, and, under protection, first here (below).
        var selection = evaluation.SelectionState switch
        {
            JavaScriptOutcomeSelectionState.Named => OutcomeSelection.Named(evaluation.OutcomeNames),
            JavaScriptOutcomeSelectionState.Empty => OutcomeSelection.Empty,
            _ => OutcomeSelection.Unspecified
        };

        // Spec 1229 FR-006: an outcome name can come from a protected value the script read or received
        // (`complete(x, variables.customer.tier)`). The projection's refusal of a name outside the outcome set quotes it, so
        // under protection the selection is checked here, against the same set (Done plus PossibleOutcomes), and refused
        // without naming it.
        if ((context.HasProtectedInputs || session?.HasReadProtectedValue == true || workflowState?.HasReadProtectedValue == true) && !IsValidSelection(evaluation))
            return ActivityTransition.Fault<RunJavaScriptResult>(new ActivityFault(JavaScriptCompletionException.FaultCode,
                "The script selected an outcome that is not in this node's outcome set, or Done together with another outcome. " +
                "The names are withheld because a value it read is protected."));
        return ActivityTransition.Complete(new RunJavaScriptResult(evaluation.Value), selection);
    }

    // The projection's outcome-selection rules (ActivityOutcomeSelectionRules.Validate) over this node's outcome set:
    // Done plus PossibleOutcomes, the set pinned at publication.
    private bool IsValidSelection(JavaScriptScriptEvaluationResult evaluation)
    {
        if (evaluation.SelectionState != JavaScriptOutcomeSelectionState.Named)
            return true;
        var names = evaluation.OutcomeNames;
        if (names.Count > 1 && names.Contains(OutcomeSelection.DefaultOutcome, StringComparer.Ordinal))
            return false;
        return names.All(name => StringComparer.Ordinal.Equals(name, OutcomeSelection.DefaultOutcome) ||
                                 PossibleOutcomes?.Contains(name, StringComparer.Ordinal) == true);
    }

    // A read the session refused is the cause of a failed step, even when the script caught the error and failed
    // otherwise (spec 1222 FR-017a). Its message is the session's own and names no value; nothing the script threw is
    // attached, since that could quote a protected value it read.
    private static void ThrowIfRefused(StepScriptVariables? variables)
    {
        if (variables?.Refusal is { } refusal)
            throw new InvalidOperationException(refusal);
    }
}

/// <summary>The atomic, persistable result returned by a JavaScript activity.</summary>
public sealed record RunJavaScriptResult
{
    public RunJavaScriptResult(JsonElement? value) => Value = value?.Clone();

    [Output(Key = "result", Path = "value", IsRequired = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Value { get; }
}

internal sealed class RedactedScriptFailureException(string message) : InvalidOperationException(message), Elsa.Expressions.Core.Contracts.IValueFreeExpressionFailure
{
    public string ValueFreeMessage => Message;
}
