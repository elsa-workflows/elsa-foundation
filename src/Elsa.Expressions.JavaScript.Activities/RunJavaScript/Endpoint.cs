using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.JavaScript.Activities.RunJavaScript.TestClasses;
using Elsa.FastEndpoints.Abstractions;

namespace Elsa.Expressions.JavaScript.Activities.RunJavaScript
{
    internal sealed class Endpoint(IServiceProvider serviceProvider) : ElsaEndpoint<RequestModel>
    {
        public override void Configure()
        {
            Post("javascript/execute");
            AllowAnonymous();
        }

        public override async Task HandleAsync(RequestModel req, CancellationToken ct)
        {
            if(string.IsNullOrWhiteSpace(req.Script))
            {
                await Send.ResponseAsync(
                    new { error = "Script is null" },
                    400,
                    ct
                );
                return;
            }

            try
            {
                var scriptInput = new ActivityInput<string>(
                    new BlockReference(req.Script)
                );
                var activity = new Activity(scriptInput);
                var executionContext = new ScriptExecutionContext(serviceProvider);

                await ((IActivity)activity).ExecuteAsync(executionContext);

                var result = executionContext.Get(activity.Result);

                await Send.ResponseAsync(
                    new { success = true, result }    ,
                    200,
                    ct
                );
            }
            catch(Exception e)
            {
                await Send.ResponseAsync(
                   new { success = false, message = e.Message },
                   500,
                   ct
               );
            }
        }
    }

    internal sealed class RequestModel
    {
        public RequestModel()
        {            
        }

        public string? Script { get; set; }
    }
}
