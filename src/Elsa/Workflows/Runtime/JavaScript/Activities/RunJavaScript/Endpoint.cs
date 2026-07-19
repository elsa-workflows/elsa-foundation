using System.Text.Json;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript;

internal sealed class Endpoint(IJavaScriptScriptEvaluator evaluator, ILogger<Endpoint> logger) : ElsaEndpoint<RequestModel>
{
    public override void Configure()
    {
        Post("javascript/execute");
        ConfigurePermissions();
    }

    public override async Task HandleAsync(RequestModel req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Script))
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
            var arguments = JsonSerializer.SerializeToElement(new { });
            var result = await evaluator.EvaluateAsync(
                new JavaScriptScriptEvaluationRequest(req.Script, arguments, ct));

            await Send.ResponseAsync(
                new { success = true, value = result },
                200,
                ct
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "JavaScript script execution failed.");
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
