using Elsa.Activities.Core;
using Elsa.Activities.Core.Constants;
using Elsa.Activities.Core.Contracts;
using Elsa.Activities.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Activities
{
    /// <summary>
    /// Executes JavaScript code.
    /// </summary>    
    public sealed class RunJavaScriptActivity : CodeActivity<object?>
    {
        /// <inheritdoc />
        public RunJavaScriptActivity(/*[CallerFilePath] string? source = null, [CallerLineNumber] int? line = null*/) : base(/*source, line*/)
        {
        }

        /// <inheritdoc />
        public RunJavaScriptActivity(ActivityInput<string> script/*, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null*/) 
            : this(/*source, line*/)
        {
            Script = script;
        }

        
        public ActivityInput<string>? Script { get; set; }

        /// <summary>
        /// A list of possible outcomes. Use "setOutcome()" to set the outcome. Use "setOutcomes" to set multiple outcomes.
        /// </summary>        
        public ActivityInput<ICollection<string>>? PossibleOutcomes { get; set; }

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
                additionalFunctions: RunJavaScriptFunctionsProvider.CreateSetOutcomeFunctions(context),
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

        
    }
}
