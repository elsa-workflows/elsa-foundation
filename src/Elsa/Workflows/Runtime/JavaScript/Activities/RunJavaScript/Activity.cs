using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript;

/// <summary>
/// Executes JavaScript code.
/// </summary>    
internal sealed class Activity : CodeActivity<object?>
{
    /// <inheritdoc />
    public Activity() : base()
    {
    }

    /// <inheritdoc />
    public Activity(InputArgument<string> script)
        : this()
    {
        Script = script;
    }

    /// <summary>
    /// The script that will be executed by this activity
    /// </summary>
    public InputArgument<string>? Script { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(IActivityExecutionContext context)
    {
        var script = context.Get(Script);

        // If no script was specified, there's nothing to do.
        if (string.IsNullOrWhiteSpace(script))
            return;

        // Get a JavaScript evaluator.
        var javaScriptEvaluator = context.GetRequiredService<IJavaScriptEvaluator>();

        // Run the script.
        var result = await javaScriptEvaluator.EvaluateAsync(
            script,
            typeof(object),
            context.ExpressionExecutionContext,
            options: null,
            additionalFunctions: CreateSetOutcomeFunctions(context),
            context.CancellationToken
        );

        // Set the result as output, if any.
        if (result is not null)
            context.Set(Result, result);

        // Get the outcome or outcomes set by the script, if any. If not set, use "Done".
        var outcomes = context.GetOutcomes();
        if (!outcomes.Any())
        {
            outcomes = [ActivityOutcomes.Done];
        }

        // Complete the activity with the outcome.
        var activityCompletionHandler = context.GetRequiredService<IActivityCompletionHandler>();
        await activityCompletionHandler.CompleteActivityAsync(context, outcomes);
    }

    private static IEnumerable<IJavaScriptFunction> CreateSetOutcomeFunctions(IActivityExecutionContext context)
    {
        yield return new JavaScriptFunction<string>(ActivityFunctionNames.SetOutcome, (value) => SetOutcome(context, value));
        yield return new JavaScriptFunction<string[]>(ActivityFunctionNames.SetOutcomes, (value) => SetOutcomes(context, value));
    }

    private static object? SetOutcomes(IActivityExecutionContext ctx, string[] outcomes)
    {
        ctx.SetOutcomes(outcomes);
        return null;
    }

    private static object? SetOutcome(IActivityExecutionContext ctx, string outcome)
    {
        ctx.SetOutcomes([outcome]);
        return null;
    }
}