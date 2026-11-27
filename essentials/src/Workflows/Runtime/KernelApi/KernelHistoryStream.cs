using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal static class KernelHistoryStream
{
    internal const string Route = "runtime/workflows/instances/{workflowExecutionId}/history/live";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly byte[] Newline = [(byte)'\n'];

    internal static void Map(IEndpointRouteBuilder routes) => routes.MapGet(Route, FollowAsync)
        .WithOwner(WorkflowsRuntimeKernelApiFeature.FeatureName)
        .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
        .RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)
        .WithName("WatchCommittedWorkflowHistory");

    private static async Task FollowAsync([AsParameters] GetRuntimeHistory request, HttpContext context,
        IRuntimeHistoryPageReader history, NormalizedPrincipalValidator validator, TimeProvider time)
    {
        using var lifetime = new CancellationTokenSource(Lifetime, time);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.Token);
        var token = cancel.Token;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        if (!validator.TryGetNormalizedPrincipal(context.User, out var principal))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).Take(2).ToArray();
        var subjects = principal.FindAll(IdentityClaimTypes.UserId).Select(claim => claim.Value).Take(2).ToArray();
        if (tenants is not [var tenant] || subjects is not [var subject] || principal.HasClaim(claim => claim.Type == IdentityClaimTypes.SubjectProvider) ||
            string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(subject))
        {
            response.StatusCode = StatusCodes.Status501NotImplemented;
            return;
        }

        if (history is not KernelRuntimeHistoryPageReader kernelHistory)
        {
            response.StatusCode = StatusCodes.Status501NotImplemented;
            return;
        }

        try
        {
            var first = await ReadAsync(kernelHistory, request, CurrentStatusAsync, token);
            if (first.Status is { } status)
            {
                response.StatusCode = status;
                return;
            }

            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            response.ContentType = "application/x-ndjson; charset=utf-8";
            var page = first.Page!;
            // An idle connection acknowledges the exact bookmark it opened without re-sealing it.
            if (page is { ScannedCount: 0, NextContinuationToken: null } && request.Cursor is { } bookmark)
                first = first with { Page = page with { CatchUpToken = bookmark } };
            await WriteAsync(first);
            while (true)
            {
                var cursor = page.NextContinuationToken ?? page.CatchUpToken;
                if (cursor is null) return;
                if (page.NextContinuationToken is null) await Task.Delay(IdleDelay, time, token);

                // Each page opens its own bounded committed read and checks the current access binding.
                // Advance through scanned-but-filtered entries too. Idle probes do not send a new bookmark.
                var next = await ReadAsync(kernelHistory, request with { Cursor = cursor }, CurrentStatusAsync, token);
                if (next.Page is not { } current)
                {
                    await WriteAsync(next);
                    return;
                }
                if (current.ScannedThroughSequence != page.ScannedThroughSequence || current.Items.Count != 0)
                    await WriteAsync(next);
                page = current;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!context.RequestAborted.IsCancellationRequested && !response.HasStarted)
            {
                response.ContentType = null;
                response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
        }

        async Task<int?> CurrentStatusAsync(WorkflowExecutionState workflow)
        {
            // Use the route's original resource for its grant and the authorized run for structure.
            // The reader invokes this before journal access and again against the verified run before disclosure.
            await using var scope = context.RequestServices.CreateAsyncScope();
            var current = scope.ServiceProvider.GetRequiredService<ICurrentSubjectPermissionAuthorizer>();
            var routeGrant = await current.AuthorizeAsync(new(tenant, subject, WorkflowRuntimePermissions.WorkflowRuntimeRead, context), token);
            if (routeGrant.Status != CurrentSubjectPermissionStatus.Granted) return Refusal(routeGrant.Status);
            var structureGrant = await current.AuthorizeAsync(new(tenant, subject,
                HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission, workflow), token);
            return structureGrant.Status == CurrentSubjectPermissionStatus.Granted ? null : Refusal(structureGrant.Status);
        }

        async Task WriteAsync(KernelHistoryStreamFrame frame)
        {
            await JsonSerializer.SerializeAsync(response.Body, frame, KernelHistoryStreamJson.Default.KernelHistoryStreamFrame, token);
            await response.Body.WriteAsync(Newline, token);
            await response.Body.FlushAsync(token);
        }
    }

    private static int Refusal(CurrentSubjectPermissionStatus status) => status == CurrentSubjectPermissionStatus.Unsupported
        ? StatusCodes.Status501NotImplemented : StatusCodes.Status403Forbidden;

    private static async Task<KernelHistoryStreamFrame> ReadAsync(KernelRuntimeHistoryPageReader history,
        GetRuntimeHistory request, Func<WorkflowExecutionState, Task<int?>> currentPermission, CancellationToken token)
    {
        try
        {
            var (page, refusal) = await history.ReadPageAsync(request, currentPermission, token);
            if (refusal is { } status) return new(null, status);
            return page switch
            {
                null => new(null, StatusCodes.Status404NotFound),
                { Coverage.State: "invalid" } => new(null, StatusCodes.Status409Conflict),
                { Coverage.PolicyId: KernelHistoryJournal.Policy } => new(page, null),
                _ => new(null, StatusCodes.Status501NotImplemented)
            };
        }
        catch (ArgumentException) { return new(null, StatusCodes.Status400BadRequest); }
        catch (RuntimeHistoryUnavailableException) { return new(null, StatusCodes.Status503ServiceUnavailable); }
    }
}

internal sealed record KernelHistoryStreamFrame(RuntimeHistoryPageView? Page, int? Status);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KernelHistoryStreamFrame))]
internal partial class KernelHistoryStreamJson : JsonSerializerContext;
