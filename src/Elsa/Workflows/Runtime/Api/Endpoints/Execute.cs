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

/// <summary>
/// <c>POST runtime/workflows/executables/{artifactId}/execute</c> — starts a published workflow by artifact id.
/// The call is <b>synchronous to quiescence</b>: the in-process actor drains the run inline before the response is
/// written (ADR 0031 sticky single-writer drain), so by the time the caller receives a response the workflow has
/// already reached its first stopping point — completion, fault, or a durable suspension. The response is therefore
/// not an async hand-off acknowledgement; its <see cref="WorkflowExecutionStartDispatchView.CommandDispatchStatus"/>
/// reflects the actual drain outcome. Status-code mapping honours this: a <c>Rejected</c> dispatch maps to
/// <c>409 Conflict</c> (a state conflict — retired/unpublished/expired reference or a rejected command), and every
/// other outcome (<c>Accepted</c>, <c>AcceptedButFaulted</c>, <c>Duplicate</c>, <c>Deferred</c>) maps to
/// <c>200 OK</c> — the drain ran and the returned status describes what happened. <c>AcceptedButFaulted</c> in
/// particular still carries a 200 body: the dispatch and drain completed, but the workflow ended the turn faulted,
/// which callers detect by inspecting <c>CommandDispatchStatus</c> rather than the HTTP code.
/// <c>GET runtime/workflows/instances/{workflowExecutionId}</c> remains the polling surface for subsequent state.
/// </summary>
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
        Post(RouteConstants.GetRoute("executables/{artifactId}/execute"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeExecute);
    }

    public override async Task HandleAsync(ExecuteWorkflow req, CancellationToken ct)
    {
        try
        {
            var result = await _requestSender.Send(req, ct);
            var dispatchStatus = Enum.Parse<WorkflowExecutionCommandDispatchStatus>(result.CommandDispatchStatus);

            // The drain is synchronous to quiescence (ADR 0031), so the run has already reached completion, fault, or a
            // durable suspension by the time we respond. 200 honestly reports "handled, here is the outcome" — a 202
            // "Accepted" would falsely advertise an async hand-off. A Rejected dispatch is a state conflict (409); every
            // other outcome (Accepted, AcceptedButFaulted, Duplicate, Deferred) is 200 with the outcome in the body.
            var statusCode = dispatchStatus == WorkflowExecutionCommandDispatchStatus.Rejected ? 409 : 200;
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
