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
/// </remarks>
public sealed class RunJavaScript(IJavaScriptScriptEvaluator evaluator) : Activity<RunJavaScriptResult>
{
    /// <summary>The stable activity type key, resolved by the runtime's CLR activity constructor.</summary>
    public const string ActivityType = "Elsa.RunJavaScript";

    /// <summary>The JavaScript program to execute.</summary>
    [ActivityInput(Key = "script")]
    public required string Script { get; set; }

    /// <summary>The complete explicit argument object exposed to the program as read-only <c>args</c>.</summary>
    [ActivityInput(Key = "arguments", DefaultValue = "{}")]
    public JsonElement Arguments { get; set; } = JsonSerializer.SerializeToElement(new { });

    protected override async ValueTask<ActivityTransition<RunJavaScriptResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var result = string.IsNullOrWhiteSpace(Script)
            ? null
            : await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                Script,
                Arguments,
                context.CancellationToken));
        return ActivityTransition.Complete(new RunJavaScriptResult(result));
    }
}

/// <summary>The atomic, persistable result returned by a JavaScript activity.</summary>
public sealed record RunJavaScriptResult
{
    public RunJavaScriptResult(JsonElement? value) => Value = value?.Clone();

    [Output(Key = "result", Path = "value", IsRequired = false)]
    public JsonElement? Value { get; }
}
