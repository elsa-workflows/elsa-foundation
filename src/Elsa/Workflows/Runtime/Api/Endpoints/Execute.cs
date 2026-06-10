using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class Execute : ElsaRequestHandlerEndpoint<ExecuteWorkflow, WorkflowExecutionView>
{
    private readonly IRequestSender _requestSender;
    private readonly ILogger<Execute> _logger;

    public Execute(IRequestSender requestSender, ILogger<Execute> logger) : base(requestSender, logger)
    {
        _requestSender = requestSender;
        _logger = logger;
    }

    public override void Configure()
    {
        Post(RouteConstants.GetRoute("{artifactId}/execute"));
        ConfigurePermissions();
    }

    public override async Task HandleAsync(ExecuteWorkflow req, CancellationToken ct)
    {
        try
        {
            var result = await _requestSender.Send(req, ct);
            var statusCode = result.Status == WorkflowExecutionResultStatus.Faulted.ToString() ? 500 : 200;
            await Send.ResponseAsync(result, statusCode, ct);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(ExecuteWorkflow));
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
