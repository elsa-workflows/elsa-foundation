using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Handlers.Alterations;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Alterations;

internal sealed class SubmitWorkflowAlterationPlanEndpoint(
    IRequestSender requestSender,
    ILogger<SubmitWorkflowAlterationPlanEndpoint> logger)
    : WorkflowAlterationEndpoint<SubmitWorkflowAlterationPlan, WorkflowAlterationPlanSubmissionView>
{
    public override void Configure()
    {
        Post(AlterationRouteConstants.Plans);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeManage);
    }

    public override async Task HandleAsync(SubmitWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        var key = HttpContext.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            await WriteProblemAsync("MissingIdempotencyKey", "The Idempotency-Key header is required.", StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }
        if (key.Length > SubmitWorkflowAlterationPlanRequestHandler.MaximumIdempotencyKeyLength)
        {
            await WriteProblemAsync("InvalidIdempotencyKey", "The Idempotency-Key header must not exceed 256 characters.", StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        try
        {
            var response = await requestSender.Send(request with { IdempotencyKey = key }, cancellationToken);
            HttpContext.Response.Headers.Location = response.Links.Self;
            await Send.ResponseAsync(response, StatusCodes.Status202Accepted, cancellationToken);
        }
        catch (WorkflowAlterationAdmissionRejectedException exception)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((exception.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds));
            HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await WriteProblemAsync("AlterationAdmissionBackpressure", "Runtime alteration admission is temporarily at capacity.", StatusCodes.Status429TooManyRequests, cancellationToken);
        }
        catch (WorkflowAlterationIdempotencyConflictException)
        {
            await WriteProblemAsync("AlterationIdempotencyConflict", "The idempotency key is already associated with a different alteration request.", StatusCodes.Status409Conflict, cancellationToken);
        }
        catch (ArgumentException)
        {
            await WriteProblemAsync("InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await WriteProblemAsync("InvalidAlterationRequest", "The alteration request is invalid.", StatusCodes.Status422UnprocessableEntity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure submitting a runtime alteration plan.");
            await WriteProblemAsync("UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }
}

internal sealed class GetWorkflowAlterationPlanEndpoint(
    IRequestSender requestSender,
    ILogger<GetWorkflowAlterationPlanEndpoint> logger)
    : WorkflowAlterationEndpoint<GetWorkflowAlterationPlan, WorkflowAlterationPlanView>
{
    public override void Configure()
    {
        Get(AlterationRouteConstants.Plan);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetWorkflowAlterationPlan request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(request, cancellationToken);
            await Send.OkAsync(response, cancellationToken);
        }
        catch (WorkflowAlterationResourceNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            await WriteProblemAsync("InvalidAlterationPlanId", "The alteration plan identifier is invalid.", StatusCodes.Status400BadRequest, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure reading a runtime alteration plan.");
            await WriteProblemAsync("UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }
}

internal sealed class PageWorkflowAlterationJobsEndpoint(
    IRequestSender requestSender,
    ILogger<PageWorkflowAlterationJobsEndpoint> logger)
    : WorkflowAlterationEndpoint<PageWorkflowAlterationJobs, WorkflowAlterationJobPageView>
{
    public override void Configure()
    {
        Get(AlterationRouteConstants.JobsPage);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(PageWorkflowAlterationJobs request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(request, cancellationToken);
            await Send.OkAsync(response, cancellationToken);
        }
        catch (WorkflowAlterationResourceNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            await WriteProblemAsync("InvalidAlterationJobsPage", "The alteration jobs page request is invalid.", StatusCodes.Status400BadRequest, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure paging runtime alteration jobs.");
            await WriteProblemAsync("UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }
}

internal sealed class GetWorkflowAlterationJobEndpoint(
    IRequestSender requestSender,
    ILogger<GetWorkflowAlterationJobEndpoint> logger)
    : WorkflowAlterationEndpoint<GetWorkflowAlterationJob, WorkflowAlterationJobView>
{
    public override void Configure()
    {
        Get(AlterationRouteConstants.Job);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeRead);
    }

    public override async Task HandleAsync(GetWorkflowAlterationJob request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(request, cancellationToken);
            await Send.OkAsync(response, cancellationToken);
        }
        catch (WorkflowAlterationResourceNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            await WriteProblemAsync("InvalidAlterationJobId", "The alteration job identifier is invalid.", StatusCodes.Status400BadRequest, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure reading a runtime alteration job.");
            await WriteProblemAsync("UnexpectedError", "Unexpected error occurred.", StatusCodes.Status500InternalServerError, cancellationToken);
        }
    }
}

internal sealed class CancelWorkflowAlterationPlanEndpoint(
    IRequestSender requestSender,
    ILogger<CancelWorkflowAlterationPlanEndpoint> logger)
    : ElsaEndpointWithoutRequest<WorkflowAlterationPlanView>
{
    public override void Configure()
    {
        Post(AlterationRouteConstants.Cancel);
        ConfigurePermissions(PermissionNames.WorkflowRuntimeManage);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new CancelWorkflowAlterationPlan(Route<string>("planId")!);
            var response = await requestSender.Send(request, cancellationToken);
            await Send.ResponseAsync(response.Plan, response.IsTerminalNoOp ? StatusCodes.Status200OK : StatusCodes.Status202Accepted, cancellationToken);
        }
        catch (WorkflowAlterationResourceNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            await WorkflowAlterationEndpointProblems.WriteAsync(
                HttpContext,
                "InvalidAlterationPlanId",
                "The alteration plan identifier is invalid.",
                StatusCodes.Status400BadRequest,
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure cancelling a runtime alteration plan.");
            await WorkflowAlterationEndpointProblems.WriteAsync(
                HttpContext,
                "UnexpectedError",
                "Unexpected error occurred.",
                StatusCodes.Status500InternalServerError,
                cancellationToken);
        }
    }
}

internal abstract class WorkflowAlterationEndpoint<TRequest, TResponse> : ElsaEndpoint<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    protected async Task WriteProblemAsync(string code, string message, int statusCode, CancellationToken cancellationToken)
        => await WorkflowAlterationEndpointProblems.WriteAsync(HttpContext, code, message, statusCode, cancellationToken);
}

internal static class WorkflowAlterationEndpointProblems
{
    public static async Task WriteAsync(
        HttpContext context,
        string code,
        string message,
        int statusCode,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new WorkflowAlterationProblemView(code, message, statusCode), cancellationToken);
    }
}
