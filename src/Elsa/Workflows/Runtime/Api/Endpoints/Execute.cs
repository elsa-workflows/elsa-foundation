using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

internal sealed class Execute : ElsaRequestHandlerEndpoint<ExecuteWorkflow, WorkflowExecutionStartDispatchView>
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
        ConfigurePermissions(PermissionNames.WorkflowRuntimeExecute);
    }

    public override async Task HandleAsync(ExecuteWorkflow req, CancellationToken ct)
    {
        try
        {
            var result = await _requestSender.Send(req, ct);
            var dispatchStatus = Enum.Parse<WorkflowExecutionCommandDispatchStatus>(result.CommandDispatchStatus);
            var statusCode = dispatchStatus == WorkflowExecutionCommandDispatchStatus.Rejected ? 409 : 202;
            await Send.ResponseAsync(result, statusCode, ct);
        }
        catch (WorkflowExecutableNotFoundException e)
        {
            ThrowError(e, 400);
        }
        catch (WorkflowExecutableReferenceRejectedException e)
        {
            // Reference gate refusal (ADR 0040): the artifact exists but is not dispatchable under the required
            // scope (retired, unpublished, or expired test run). 409 mirrors the Rejected dispatch-status mapping
            // above — a state conflict, not a bad request.
            ThrowError(e, 409);
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
