using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.StandaloneTriggers;

/// <summary>Request-owned HTTP transport over a checked resource reservation and the common durable Runtime ingress port.</summary>
internal sealed class StandaloneHttpTriggerMiddleware(IRouteTable routes, IRouteMatcher matcher,
    IPersistenceAccessContextAccessor access, IOptions<HttpEndpointOptions> options, HttpTriggerSourceContext sourceContext,
    ILogger<StandaloneHttpTriggerMiddleware> logger, IRuntimeIngressReservationReader? reservations = null,
    IStandaloneTriggerIngress<StandaloneHttpTriggerSource>? ingress = null, IRuntimeIngressTransportObserver? observations = null,
    IHttpEndpointAuthorizationHandler? authorization = null, IHttpRequestBodyParser? bodyParser = null,
    IPersistenceOperationScopeFactory? scopes = null, TimeProvider? timeProvider = null,
    IBookmarkStimulusIndex? waitIndex = null, IBookmarkStateStore? bookmarks = null,
    IWorkflowExecutionStateStore? executions = null, IActivityExecutionStateStore? activities = null,
    IRuntimeIngressDeliveryStore? deliveryStore = null) : IMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var mount = options.Value.BasePath.TrimEnd('/'); var path = context.Request.Path.Value ?? "";
        if (reservations?.SupportsReservations != true || mount.Length == 0 ||
            !path.StartsWith(mount + "/", StringComparison.OrdinalIgnoreCase))
        { await next(context); return; }
        StandaloneTriggerIngressResult? accepted = null; RuntimeIngressReservation? owner = null;
        string? acceptedSourceId = null;
        var exchangeId = Guid.NewGuid().ToString("N");
        var writeCompleted = false;
        try
        {
            using var lease = (routes as IRouteTableSnapshotProvider)?.AcquireSnapshot();
            var relativePath = path[mount.Length..].Trim('/');
            var match = lease?.SupportsRouteResolution == true ? lease.ResolveRoute(relativePath, context.Request.Method, matcher) :
                HttpRouteResolution.Resolve(lease?.Snapshot.Routes ?? routes.ToArray(), relativePath, context.Request.Method, matcher);
            if (match is null) { await next(context); return; }
            if (access.Current.Scope is not { } persistenceScope || access.Current.AcrossScopes)
                throw new TriggerOperationException("unavailable", "HTTP source routing requires one trusted persistence scope.");
            var hash = HttpEndpointStimulus.Hash(match.Template, context.Request.Method);
            var claims = await reservations.ListInScopeAsync(persistenceScope, new(HttpEndpointRouting.StimulusType, 2, StimulusHash: hash), context.RequestAborted);
            var standalone = claims.Items.Where(claim => claim.OwnerKind == RuntimeIngressReservation.StandaloneTriggerOwner).ToArray();
            if (standalone.Length == 0) { await next(context); return; }
            // Authorization precedes disclosure of a corrupt/ambiguous claim set.
            var protectedEndpoint = standalone.Any(claim => !bool.TryParse(claim.Metadata.GetValueOrDefault(HttpEndpointRouting.AuthorizeMetadataKey), out var requires) || requires);
            if (protectedEndpoint && (authorization is null || !await authorization.AuthorizeAsync(new(context))))
            { await ProblemAsync(context, 401, "SourceUnauthorized", "Sign in to invoke this HTTP trigger."); return; }
            owner = standalone[0];
            sourceContext.Bind(owner);
            var waits = await ObserveWaitsAsync(owner.Scope, hash, context.RequestAborted);
            var waitOptions = HttpEndpointStimulusOptions.MergeForResume(waits.Select(item => (IReadOnlyDictionary<string, string>?)item.State.Metadata));
            if (waitOptions.Authorize && (authorization is null ||
                !await HttpEndpointRequestAuthorization.AuthorizeAsync(authorization, context, waitOptions.Policies)))
            { await ProblemAsync(context, 401, "SourceUnauthorized", "Sign in to invoke this HTTP trigger."); return; }
            if (standalone.Length != 1 || claims.Next is not null || claims.Items.Any(claim => claim.OwnerKind != RuntimeIngressReservation.StandaloneTriggerOwner))
                throw new TriggerOperationException("conflict", "This HTTP endpoint has conflicting owners.");
            if (owner.Scope.Persistence != persistenceScope || owner.StimulusHash != hash ||
                owner.Metadata.GetValueOrDefault(HttpEndpointRouting.TemplateMetadataKey) != match.Template ||
                !StringComparer.OrdinalIgnoreCase.Equals(owner.Metadata.GetValueOrDefault(HttpEndpointRouting.MethodMetadataKey), context.Request.Method))
                throw new TriggerOperationException("conflict", "The HTTP route generation changed; retry against the current endpoint.");
            if (ingress is null || observations?.SupportsObservations != true || bodyParser is null || deliveryStore is null)
                throw new TriggerOperationException("unavailable", "The protected HTTP admission and transport boundary is not installed.");
            var request = await HttpEndpointRequestModelReader.ReadAsync(context, relativePath, match.RouteValues,
                waitOptions.RequestSizeLimit is { } waitLimit ? Math.Min(options.Value.MaxRequestBodyBytes, waitLimit) : options.Value.MaxRequestBodyBytes,
                ["Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key"]);
            if (request is null) { await ProblemAsync(context, 413, "BodyTooLarge", "The HTTP request exceeds the configured body limit."); return; }
            var payload = request.Body is null ? JsonSerializer.SerializeToElement<object?>(null) : bodyParser.Parse(context.Request.ContentType, request.Body);
            if (payload is null) { await ProblemAsync(context, 400, "InvalidBody", "Use a supported content type and valid request body."); return; }
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrEmpty(key)) key = Guid.NewGuid().ToString("N");
            if (key.Length > 128 || key.Any(char.IsControl)) { await ProblemAsync(context, 400, "InvalidIdempotencyKey", "The idempotency key is invalid."); return; }
            var metadata = JsonSerializer.SerializeToElement(new { method = request.Method, path = request.Path, query = request.Query,
                routeValues = request.RouteData, contentType = context.Request.ContentType }, Json);
            sourceContext.CaptureRequest(request, hash, waits.Select(RuntimeIngressWaitObservation.From).ToArray());
            acceptedSourceId = RuntimeStandaloneIngressSource.BuildOutboxItemId(owner.Scope.Persistence, owner.Scope.TenantId, owner.OwnerId,
                StandaloneHttpTriggerSource.Descriptor.SourceFamily, key);
            accepted = await ingress.AdmitAsync(new(owner.OwnerId, key, StandaloneHttpTriggerSource.Descriptor.PayloadSchema, payload.Value, metadata), context.RequestAborted);
            // The already authorized request reads its own exact physical source; the public acknowledgement carries no authority.
            var acceptedItem = await deliveryStore.FindDeliveryAsync(owner.Scope, acceptedSourceId, context.RequestAborted) ??
                throw new TriggerOperationException("unavailable", "The accepted HTTP source is unavailable.");
            var source = acceptedItem.StandaloneIngress ?? throw new TriggerOperationException("unavailable", "The accepted HTTP source is unavailable.");
            if (source.AdmissionId != accepted.AdmissionId || source.TriggerId != owner.OwnerId || source.TenantId != owner.Scope.TenantId ||
                source.SourceFamily != StandaloneHttpTriggerSource.Descriptor.SourceFamily)
                throw new InvalidDataException("The accepted HTTP source differs from its authorized registered request.");
            var delivery = acceptedItem.SubscriptionDelivery ?? throw new TriggerOperationException("unavailable", "The accepted HTTP recipients are unavailable.");
            if (delivery.Recipients.Count == 0) throw new TriggerOperationException("unavailable", "No HTTP recipient was accepted.");
            var capturedHttp = source.CanonicalPayload.GetProperty("trigger").GetProperty("configuration").GetProperty("http");
            var mode = capturedHttp.GetProperty("mode").GetString();
            await RecordAsync(owner.Scope, acceptedSourceId!, exchangeId, RuntimeIngressTransportState.AwaitingResponse, null, context.RequestAborted);
            if (mode == "AsyncFanOut")
            {
                var result = new { deliveryId = acceptedSourceId!, admission = delivery.Refusals.Count == 0 ? "Accepted" : "PartiallyAccepted",
                    admissionPolicy = source.AdmissionPolicy.ToString(), acceptedCount = delivery.Recipients.Count, refusedCount = delivery.Refusals.Count,
                    recipients = delivery.Recipients.Select(recipient => new { recipientRef = Opaque(acceptedSourceId!, recipient.SubscriptionId),
                        admission = "Accepted", reasonCode = (string?)null }).Concat(delivery.Refusals.Select(refused => new {
                            recipientRef = Opaque(acceptedSourceId!, refused.SubscriptionId), admission = "Refused", reasonCode = (string?)refused.ReasonCode })),
                    statusLocation = "runtime/standalone-trigger-deliveries/" + Uri.EscapeDataString(acceptedSourceId!), replayed = accepted.Replayed };
                await Results.Json(result, Json, statusCode: 202).ExecuteAsync(context);
            }
            else if (mode == "WorkflowResponse")
            {
                if (delivery.Recipients.Count != 1 || scopes is null) throw new TriggerOperationException("unavailable", "Workflow-owned response observation is unavailable.");
                await RespondAsync(context, owner.Scope, acceptedItem, delivery.Recipients[0].StartIdentity.WorkflowExecutionId);
            }
            else throw new TriggerOperationException("unavailable", "The captured HTTP response mode is unsupported.");
            writeCompleted = true;
            await RecordAsync(owner.Scope, acceptedSourceId!, exchangeId, RuntimeIngressTransportState.WriteCompleted,
                context.Response.StatusCode, CancellationToken.None);
        }
        catch (HttpEndpointAuthorizationConfigurationException)
        { if (!context.Response.HasStarted) await ProblemAsync(context, 500, "SourceAuthorizationUnavailable", "The HTTP source authorization configuration is unavailable."); }
        catch (StandaloneTriggerAdmissionRefusedException refused)
        {
            var invalid = refused.Refusals.Any(item => item.Stage.Equals("Input", StringComparison.OrdinalIgnoreCase) || item.Stage.Equals("Mapping", StringComparison.OrdinalIgnoreCase));
            var identity = exchangeId;
            await Results.Json(new { code = refused.Code, message = "The HTTP request did not accept any work.", acceptedCount = 0,
                refusedCount = refused.Refusals.Count, recipients = refused.Refusals.Select(item => new {
                    recipientRef = Opaque(identity, item.SubscriptionId), admission = "Refused", reasonCode = (string?)item.ReasonCode })
                    .Concat(refused.NotAcceptedSubscriptionIds.Select(id => new { recipientRef = Opaque(identity, id),
                        admission = "NotAcceptedDueToPolicy", reasonCode = (string?)"RequireAll" })) }, Json,
                statusCode: invalid ? 400 : 503).ExecuteAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        { if (accepted is not null && owner is not null) await TryRecordFailureAsync(owner.Scope, acceptedSourceId!, exchangeId, RuntimeIngressTransportState.ClientDisconnected, null); }
        catch (HttpResponseDeadlineException)
        {
            if (accepted is not null && owner is not null) await TryRecordFailureAsync(owner.Scope, acceptedSourceId!, exchangeId, RuntimeIngressTransportState.TimedOut, 504);
            if (!context.Response.HasStarted) await ProblemAsync(context, 504, "ResponseTimedOut", "The response deadline expired; accepted work continues.", accepted is null ? null : acceptedSourceId);
        }
        catch (TriggerOperationException exception)
        {
            if (accepted is not null && owner is not null) await TryRecordFailureAsync(owner.Scope, acceptedSourceId!, exchangeId,
                writeCompleted ? RuntimeIngressTransportState.ReceiptUnknown : RuntimeIngressTransportState.WriteFailed, writeCompleted ? context.Response.StatusCode : 503);
            if (!writeCompleted && !context.Response.HasStarted) await ProblemAsync(context, exception.Code switch { "unauthorized" => 401, "forbidden" => 403, "conflict" => 409, "validation" => 400, _ => 503 }, exception.Code, exception.Message, accepted is null ? null : acceptedSourceId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Standalone HTTP transport failed; accepted work, if any, remains retained.");
            if (accepted is not null && owner is not null) await TryRecordFailureAsync(owner.Scope, acceptedSourceId!, exchangeId,
                writeCompleted ? RuntimeIngressTransportState.ReceiptUnknown : RuntimeIngressTransportState.WriteFailed, writeCompleted ? context.Response.StatusCode : null);
            if (!writeCompleted && !context.Response.HasStarted) await ProblemAsync(context, 503, "SourceUnavailable", "The HTTP source is temporarily unavailable. Reuse the idempotency key to reconcile uncertain admission.", accepted is null ? null : acceptedSourceId);
        }
        finally { sourceContext.Clear(); }
    }

    private async ValueTask<IReadOnlyList<BookmarkStateObservation>> ObserveWaitsAsync(TriggerScope scope, string hash, CancellationToken ct)
    {
        if (waitIndex is null || bookmarks is null || executions is null || activities is null)
            throw new TriggerOperationException("unavailable", "Exact HTTP wait policy observation is unavailable.");
        var page = await waitIndex.ListByStimulusPageAsync(new(HttpEndpointStimulus.StimulusType, hash, RuntimeStorePageRequest.MaximumLimit), ct);
        if (page.NextContinuationToken is not null)
            throw new TriggerOperationException("unavailable", "The complete HTTP wait cohort exceeds the bounded source admission limit.");
        var observed = new List<BookmarkStateObservation>();
        var now = time.GetUtcNow();
        foreach (var candidate in page.Items)
        {
            if (candidate.ExpiresAt <= now) continue;
            var owner = await executions.FindAsync(candidate.WorkflowExecutionId, ct)
                ?? throw new TriggerOperationException("unavailable", "An HTTP wait owner is temporarily unavailable.");
            if (owner.TenantId != scope.TenantId || owner.RunKind == WorkflowRunKind.TestRun || owner.TestScope is not null) continue;
            var physical = await bookmarks.ObserveAsync(candidate.WorkflowExecutionId, candidate.BookmarkId, ct)
                ?? throw new TriggerOperationException("unavailable", "The HTTP wait cohort changed; retry the request.");
            if (physical.Scope is { } physicalScope && physicalScope != scope.Persistence)
                throw new TriggerOperationException("unavailable", "HTTP wait observation crossed physical scope.");
            var state = await activities.FindAsync(candidate.WorkflowExecutionId, candidate.ActivityExecutionId, ct)
                ?? throw new TriggerOperationException("unavailable", "An HTTP wait activity is temporarily unavailable.");
            var expected = StimulusResumeRecipient.From(physical, owner, scope.Persistence);
            if (physical.State.StimulusType != HttpEndpointStimulus.StimulusType || physical.State.StimulusHash != hash)
                throw new TriggerOperationException("unavailable", "The HTTP wait cohort changed; retry the request.");
            if (expected.MatchesAdmission(physical, owner, state, scope.Persistence, now)) observed.Add(physical);
        }
        return observed;
    }

    private async Task RespondAsync(HttpContext context, TriggerScope scope, RuntimePostCommitOutboxItem source, string executionId)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(options.Value.SyncRequestTimeout);
        try
        {
            var first = true;
            while (true)
            {
                await using var operation = await scopes!.CreateAsync(scope.Persistence, deadline.Token);
                if (first && operation.ServiceProvider.GetService<IRuntimePostCommitOutboxProcessor>() is { } processor)
                {
                    first = false;
                    // Prompt the ordinary bounded processor; recovery and peer workers retain ownership afterwards.
                    await processor.ProcessAsync(new(1, intentKind: RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind), deadline.Token);
                }
                var delivery = operation.ServiceProvider.GetRequiredService<HttpResponseInstructionDelivery>();
                if (await delivery.TryDeliverAsync(context.Response, [executionId], deadline.Token)) return;
                var state = await operation.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId, deadline.Token);
                if (state?.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Cancelled or WorkflowExecutionStatus.Faulted)
                {
                    if (await delivery.TryDeliverAsync(context.Response, [executionId], deadline.Token)) return;
                    await ProblemAsync(context, 502, "ResponseMissing", "The workflow ended without a committed HTTP response instruction.", source.OutboxItemId);
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), time, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new HttpResponseDeadlineException(); }
    }

    private ValueTask RecordAsync(TriggerScope scope, string sourceId, string exchangeId, RuntimeIngressTransportState state, int? status, CancellationToken ct) =>
        observations!.RecordAsync(scope, new(sourceId, exchangeId, state, status, time.GetUtcNow()), ct);
    private async ValueTask TryRecordFailureAsync(TriggerScope scope, string sourceId, string exchangeId, RuntimeIngressTransportState state, int? status)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await RecordAsync(scope, sourceId, exchangeId, state, status, limit.Token); }
        catch (Exception exception) { logger.LogWarning(exception, "The HTTP transport observation could not be retained."); }
    }
    private static Task ProblemAsync(HttpContext context, int status, string code, string message, string? deliveryId = null) =>
        Results.Json(new { code, message, deliveryId }, Json, statusCode: status).ExecuteAsync(context);
    private static string Opaque(string sourceId, string subscriptionId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{sourceId.Length}:{sourceId}{subscriptionId.Length}:{subscriptionId}")))[..24];
    private sealed class HttpResponseDeadlineException : Exception;
}
