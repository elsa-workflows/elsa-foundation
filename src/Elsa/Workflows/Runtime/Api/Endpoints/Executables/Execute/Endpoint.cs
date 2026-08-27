using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Executables.Execute;

[Post("runtime/workflows/executables/{artifactId}/execute")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeExecute)]
[RuntimeProblems("executing workflow", ExecutableArms = true, ArgumentDetail = "Invalid execute request.")]
public sealed class Endpoint(IWorkflowExecutionStartService starter) : ApiEndpointWithResult<ExecuteWorkflow, WorkflowExecutionStartDispatchView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "Execute";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override async Task<EndpointResult<WorkflowExecutionStartDispatchView>> HandleAsync(ExecuteWorkflow request, CancellationToken cancellationToken)
    {
        var result = await starter.ExecuteAsync(request, cancellationToken);
        if (result.Shed)
        {
            HttpContext.Response.Headers.RetryAfter = Math.Max(1, result.RetryAfterSeconds ?? 1).ToString(CultureInfo.InvariantCulture);
            return EndpointResult.Status(StatusCodes.Status429TooManyRequests, result);
        }

        var status = string.Equals(result.CommandDispatchStatus, nameof(WorkflowExecutionCommandDispatchStatus.Rejected), StringComparison.Ordinal)
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status200OK;
        return EndpointResult.Status(status, result);
    }
}
