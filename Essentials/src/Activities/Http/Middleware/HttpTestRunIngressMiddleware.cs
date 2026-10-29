using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.Middleware;

/// <summary>
/// Receives real HTTP requests for one prepared TestRun target. This ingress resolves exactly one immutable target
/// and invokes the runtime-owned dispatcher; it never consults the production route table or stimulus router.
/// </summary>
internal sealed class HttpTestRunIngressMiddleware(
    ITestRunTargetStore targetStore,
    IWorkflowTestScopeStore scopeStore,
    ITestRunTargetDispatcher dispatcher,
    IRouteMatcher routeMatcher,
    IWorkflowExecutionPartitionAccessor partitionAccessor,
    IOptions<HttpEndpointOptions> endpointOptions,
    TimeProvider timeProvider) : IMiddleware
{
    private const string Namespace = "/workflows/test-runs";
    private const string DriverId = "Elsa.HttpEndpoint";
    private const string RequestModeId = "http-request";
    // The test-run API authorization token authenticates the management request and is never copied into workflow input.
    private static readonly string[] ManagementCredentialHeaders = ["Authorization", "Cookie", "Proxy-Authorization"];
    private const string WorkflowTestRunManagementPermission = "workflows/publishing:manage";

    private readonly HttpEndpointOptions _endpointOptions = endpointOptions.Value;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var requestPath = context.Request.Path.Value ?? string.Empty;
        if (!IsInNamespace(requestPath))
        {
            await next(context);
            return;
        }

        if (!TryParseTargetPath(requestPath, out var testRunId, out var endpointPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            await HandleAsync(context, testRunId, endpointPath);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // A disconnected client has no response channel; do not turn its cancellation into a pipeline fault.
        }
    }

    private async Task HandleAsync(HttpContext context, string testRunId, string endpointPath)
    {
        if (!await EnsureAuthenticatedAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // The Publishing permission key is the canonical permission for TestRun management. Keep this HTTP module
        // independent of Publishing.Api, which owns its endpoint type and public API surface.
        var permissionAuthorization = context.RequestServices.GetService<IPermissionAuthorizationService>();
        if (permissionAuthorization is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tenantId = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        PermissionEvaluationResult permissionResult;
        try
        {
            permissionResult = await permissionAuthorization.AuthorizeAsync(
                new PermissionEvaluationContext(context.User, WorkflowTestRunManagementPermission, tenantId, context),
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Missing or unavailable authorization fails closed before resolving the target id.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!permissionResult.Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var target = await targetStore.FindAsync(testRunId, context.RequestAborted);
        if (target is null || !IsExactHttpTarget(target, testRunId)
            || !StringComparer.Ordinal.Equals(target.Scope.TenantId, tenantId)
            || !StringComparer.Ordinal.Equals(target.Scope.Partition.Value, partitionAccessor.Current.Value))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var now = timeProvider.GetUtcNow();
        var scope = await scopeStore.FindAsync(testRunId, context.RequestAborted);
        if (target.State == TestRunTargetState.Stopped
            || target.Scope.IsExpired(now)
            || scope is null
            || scope.State != WorkflowTestScopeState.Open
            || scope.Scope.IsExpired(now)
            || !WorkflowTestScope.ContextEquals(scope.Scope, target.Scope))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        var authoredOptions = HttpEndpointStimulusOptions.FromMetadata(target.Binding.Metadata);
        var clientAborted = context.RequestAborted;
        var requestTimeout = authoredOptions.RequestTimeout is { } authoredTimeout && authoredTimeout > TimeSpan.Zero
            ? authoredTimeout
            : (TimeSpan?)null;
        var sync = authoredOptions.ResponseMode == ResponseMode.Sync;
        if (sync)
            requestTimeout = requestTimeout is { } shorter && shorter < _endpointOptions.SyncRequestTimeout
                ? shorter
                : _endpointOptions.SyncRequestTimeout;

        using var timeoutSource = requestTimeout is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(clientAborted)
            : null;
        if (sync && timeoutSource is not null)
        {
            timeoutSource.CancelAfter(requestTimeout!.Value);
            context.RequestAborted = timeoutSource.Token;
        }

        try
        {
            await HandleTargetAsync(context, testRunId, endpointPath, target, authoredOptions, timeoutSource);
        }
        catch (OperationCanceledException) when (!clientAborted.IsCancellationRequested && timeoutSource?.IsCancellationRequested == true)
        {
            context.RequestAborted = clientAborted;
            if (context.Response.HasStarted)
                context.Abort();
            else
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
        }
        finally
        {
            context.RequestAborted = clientAborted;
        }
    }

    private async Task HandleTargetAsync(
        HttpContext context,
        string testRunId,
        string endpointPath,
        TestRunTarget target,
        HttpEndpointStimulusOptions authoredOptions,
        CancellationTokenSource? timeoutSource)
    {
        if (authoredOptions.Authorize && !await AuthorizeEndpointAsync(context, authoredOptions.Policy))
            return;

        var template = target.Binding.Metadata.GetValueOrDefault(HttpEndpointRouting.TemplateMetadataKey);
        var authoredMethod = target.Binding.Metadata.GetValueOrDefault(HttpEndpointRouting.MethodMetadataKey);
        if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(authoredMethod))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var routeValues = routeMatcher.Match(template, "/" + endpointPath);
        if (routeValues is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(context.Request.Method, authoredMethod))
        {
            context.Response.Headers["Allow"] = authoredMethod.ToUpperInvariant();
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var maximumBodyBytes = authoredOptions.RequestSizeLimit is { } authoredLimit
            ? Math.Min(_endpointOptions.MaxRequestBodyBytes, authoredLimit)
            : _endpointOptions.MaxRequestBodyBytes;
        var requestModel = await HttpEndpointRequestModelReader.ReadAsync(
            context,
            endpointPath,
            routeValues.ToDictionary(item => item.Key, item => item.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            maximumBodyBytes,
            ManagementCredentialHeaders);
        if (requestModel is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var stimulusInput = JsonSerializer.SerializeToElement(requestModel);
        if (authoredOptions.ResponseMode != ResponseMode.Sync)
            timeoutSource?.CancelAfter(authoredOptions.RequestTimeout!.Value);
        var cancellationToken = timeoutSource?.Token ?? context.RequestAborted;

        TestRunTargetDispatchResult dispatchResult;
        try
        {
            dispatchResult = await dispatcher.DispatchAsync(
                testRunId,
                stimulusInput,
                new WorkflowExecutionCommandDispatchOptions(context.RequestServices),
                cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        catch (TestScopeAdmissionException)
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }
        catch (InvalidOperationException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var responseWasAlreadyStarted = context.Response.HasStarted;
        var responseHandlingFinished = false;
        try
        {
            if (authoredOptions.ResponseMode == ResponseMode.Sync
                && context.RequestServices.GetService<HttpResponseInstructionDelivery>() is { } delivery
                && await delivery.TryDeliverAsync(context.Response, [dispatchResult.Dispatch.WorkflowExecutionId], cancellationToken, requestModel.ResponseExchangeId))
            {
                responseHandlingFinished = true;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                testRunId,
                workflowExecutionId = dispatchResult.Dispatch.WorkflowExecutionId,
                alreadyDispatched = dispatchResult.AlreadyDispatched
            }), context.RequestAborted);
            responseHandlingFinished = true;
        }
        finally
        {
            if (context.RequestServices.GetService<HttpResponseDeliveryEvidence>() is { } evidence)
                await evidence.RecordClosedAsync(dispatchResult.Dispatch.WorkflowExecutionId, requestModel.ResponseExchangeId,
                    responseHandlingFinished && !responseWasAlreadyStarted ? authoredOptions.ResponseMode == ResponseMode.Sync ? "sync" : "async" : "unknown", CancellationToken.None);
        }

    }

    private static async ValueTask<bool> EnsureAuthenticatedAsync(HttpContext context)
    {
        if (context.User.Identities.Any(identity => identity.IsAuthenticated))
            return true;

        try
        {
            var result = await context.AuthenticateAsync();
            if (!result.Succeeded || result.Principal is null)
                return false;
            context.User = result.Principal;
            return context.User.Identities.Any(identity => identity.IsAuthenticated);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async ValueTask<bool> AuthorizeEndpointAsync(HttpContext context, string? policy)
    {
        var handler = context.RequestServices.GetService<IHttpEndpointAuthorizationHandler>();
        if (handler is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        try
        {
            var policies = string.IsNullOrWhiteSpace(policy) ? Array.Empty<string>() : [policy];
            if (await HttpEndpointRequestAuthorization.AuthorizeAsync(handler, context, policies))
                return true;
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }
        catch (HttpEndpointAuthorizationConfigurationException)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return false;
        }
    }

    private static bool IsExactHttpTarget(TestRunTarget target, string testRunId) =>
        StringComparer.Ordinal.Equals(target.TestRunId, testRunId)
        && StringComparer.Ordinal.Equals(target.DriverId, DriverId)
        && StringComparer.Ordinal.Equals(target.ProviderId, DriverId)
        && StringComparer.Ordinal.Equals(target.ModeId, RequestModeId)
        && StringComparer.Ordinal.Equals(target.Scope.ScopeId, testRunId)
        && StringComparer.Ordinal.Equals(target.Binding.StimulusType, HttpEndpointStimulus.StimulusType)
        && StringComparer.Ordinal.Equals(target.Executable.ArtifactId, target.Binding.ArtifactId)
        && target.Binding.Metadata.ContainsKey(HttpEndpointRouting.TemplateMetadataKey)
        && target.Binding.Metadata.ContainsKey(HttpEndpointRouting.MethodMetadataKey);

    private static bool IsInNamespace(string path) =>
        path.StartsWith(Namespace, StringComparison.OrdinalIgnoreCase)
        && (path.Length == Namespace.Length || path[Namespace.Length] == '/');

    private static bool TryParseTargetPath(string path, out string testRunId, out string endpointPath)
    {
        testRunId = string.Empty;
        endpointPath = string.Empty;
        var remainder = path[Namespace.Length..].TrimStart('/');
        var separator = remainder.IndexOf('/');
        if (separator <= 0)
            return false;
        testRunId = remainder[..separator];
        var protocolPath = remainder[(separator + 1)..];
        const string protocolSegment = "http/";
        if (!protocolPath.StartsWith(protocolSegment, StringComparison.OrdinalIgnoreCase))
            return false;
        endpointPath = protocolPath[protocolSegment.Length..].Trim('/');
        // HttpEndpoint currently cannot publish a root route: NormalizeTemplate("/") returns empty, then
        // HttpEndpointStimulus.Hash rejects that normalized value. Keep this namespace restricted to publishable
        // non-empty endpoint paths until the trigger identity contract supports a root route.
        return testRunId.Length > 0 && endpointPath.Length > 0;
    }
}
