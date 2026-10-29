using System.Text.Json;
using System.Collections.Concurrent;
using Elsa.Activities.Http.Activities;
using Elsa.Primitives.Models;
using Elsa.Activities.Http.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Http.Services;

/// <summary>Retains transport facts without mutating activity continuation state or leaking a live request into an attempt.</summary>
public sealed class HttpResponseDeliveryEvidence(
    IActivityExecutionStateStore activities,
    IActivityExecutionInspectionStore inspections,
    IActivityExecutionInspectionWriter writer,
    TimeProvider clock,
    ILogger<HttpResponseDeliveryEvidence> logger)
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _written = new(StringComparer.Ordinal);

    public const string DeliveryKind = "http.response.delivery";
    public const string ExchangeKind = "http.response.exchange";
    private static readonly string EndpointType = TypeAliasConvention.CanonicalAlias(typeof(HttpEndpoint));
    private static bool IsEndpoint(ActivityExecutionState state) => state.Execution.ActivityType == HttpEndpoint.ActivityType || state.Execution.ActivityType == EndpointType;
    private static readonly string ResponseType = TypeAliasConvention.CanonicalAlias(typeof(HttpResponseInstruction));

    public Task RecordWrittenAsync(ActivityExecutionState response, string? exchangeId, CancellationToken cancellationToken = default) =>
        ObserveAsync(async token =>
        {
            if (exchangeId is null) return;
            _written.GetOrAdd(exchangeId, _ => new(StringComparer.Ordinal))[response.InvocationId] = 0;
            await AppendOnceAsync(response.Execution.WorkflowExecutionId, response.InvocationId,
                new($"http-written:{exchangeId}:{response.InvocationId}", DeliveryKind, "http.response.written", IncidentSeverity.Information,
                    "Written to the HTTP response. This records the server write, not acknowledgement by the client.", clock.GetUtcNow(),
                    new Dictionary<string, string> { ["exchangeId"] = exchangeId }), token);
        }, cancellationToken);

    public Task RecordClosedAsync(string workflowExecutionId, string? exchangeId, string mode, CancellationToken cancellationToken = default) =>
        ObserveAsync(async token =>
        {
            if (exchangeId is null) return;
            var states = await activities.ListAllAsync(workflowExecutionId, token);
            foreach (var endpoint in states.Where(state => ReadExchangeId(state) == exchangeId))
                await AppendOnceAsync(workflowExecutionId, endpoint.InvocationId,
                    new($"http-closed:{exchangeId}", ExchangeKind, "http.response.exchange-closed", IncidentSeverity.Information,
                        "The originating HTTP exchange has finished its workflow response handling.", clock.GetUtcNow(),
                        new Dictionary<string, string> { ["exchangeId"] = exchangeId, ["mode"] = mode,
                            ["writtenInvocationIds"] = JsonSerializer.Serialize(_written.TryGetValue(exchangeId, out var written) ? written.Keys.ToArray() : []) }), token);
            // Closure is durable before scanning completions. A response that commits after this scan
            // is reconciled by its post-commit scheduler drain; an earlier completion is visible here.
            await ReconcileCoreAsync(workflowExecutionId, token);
        }, cancellationToken);

    public Task ReconcileAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        ObserveAsync(token => ReconcileCoreAsync(workflowExecutionId, token), cancellationToken);

    private async Task ReconcileCoreAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var states = await activities.ListAllAsync(workflowExecutionId, cancellationToken);
        // No heuristic attribution across multiple endpoint invocations, loops or parallel exchanges.
        // Exact writes still have evidence, but missing/ambiguous originating exchanges remain Unknown.
        var endpoints = states.Where(IsEndpoint).ToArray();
        if (endpoints.Length != 1 || ReadExchangeId(endpoints[0]) is not { } exchangeId) return;
        var endpoint = await inspections.FindAsync(workflowExecutionId, endpoints[0].InvocationId, cancellationToken);
        var closed = endpoint?.Observations.FirstOrDefault(fact => fact.ObservationId == $"http-closed:{exchangeId}");
        if (closed is null || closed.Metadata.GetValueOrDefault("mode") is not ("sync" or "async")) return;
        var knownWrites = JsonSerializer.Deserialize<string[]>(closed.Metadata.GetValueOrDefault("writtenInvocationIds") ?? "[]") ?? [];
        foreach (var response in states.Where(IsResponse))
        {
            var projection = await inspections.FindAsync(workflowExecutionId, response.InvocationId, cancellationToken);
            if (projection is null || projection.Observations.Any(fact => fact.Code == "http.response.written")) continue;
            if (knownWrites.Contains(response.InvocationId, StringComparer.Ordinal))
            {
                await AppendOnceAsync(workflowExecutionId, response.InvocationId,
                    new($"http-written:{exchangeId}:{response.InvocationId}", DeliveryKind, "http.response.written", IncidentSeverity.Information,
                        "Written to the HTTP response. This records the server write, not acknowledgement by the client.", closed.OccurredAt,
                        new Dictionary<string, string> { ["exchangeId"] = exchangeId }), cancellationToken);
                continue;
            }
            var message = closed.Metadata.GetValueOrDefault("mode") == "async"
                ? "Response prepared but not sent: the endpoint accepted the request in the background."
                : "Response prepared but not sent: the original HTTP exchange is closed.";
            var at = response.CompletedAt > closed.OccurredAt ? response.CompletedAt.Value : closed.OccurredAt;
            await AppendOnceAsync(workflowExecutionId, response.InvocationId,
                new($"http-undelivered:{exchangeId}:{response.InvocationId}", DeliveryKind, "http.response.undelivered", IncidentSeverity.Warning,
                    message, at, new Dictionary<string, string> { ["exchangeId"] = exchangeId }), cancellationToken);
        }
    }

    private async Task AppendOnceAsync(string workflowExecutionId, string invocationId, ActivityExecutionObservation fact, CancellationToken cancellationToken)
    {
        var projection = await inspections.FindAsync(workflowExecutionId, invocationId, cancellationToken);
        if (projection?.Observations.Any(existing => existing.ObservationId == fact.ObservationId) == true) return;
        await writer.TryAppendObservationAsync(workflowExecutionId, invocationId, fact, cancellationToken);
    }

    private static bool IsResponse(ActivityExecutionState state) => state.Status == ActivityExecutionStatus.Completed
        && state.Completion?.Result.Type.Alias == ResponseType;

    private static string? ReadExchangeId(ActivityExecutionState state)
    {
        if (!IsEndpoint(state) || state.Completion?.Result.InlineValue is not { ValueKind: JsonValueKind.Object } result
            || !result.TryGetProperty("request", out var request) || request.ValueKind != JsonValueKind.Object
            || !request.TryGetProperty("responseExchangeId", out var exchange) || exchange.ValueKind != JsonValueKind.String) return null;
        return exchange.GetString();
    }

    private async Task ObserveAsync(Func<CancellationToken, Task> capture, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try { await capture(budget.Token); }
        catch (Exception exception)
        {
            // Evidence capture is observational. Failure leaves Unknown, never a false warning or an activity retry.
            logger.LogWarning(exception, "HTTP response delivery evidence could not be retained; delivery status may be unknown.");
        }
    }
}

internal sealed class HttpResponseDeliveryDrainObserver(HttpResponseDeliveryEvidence evidence) : IWorkflowSchedulerDrainObserver
{
    public async ValueTask OnDrainedAsync(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default) => await evidence.ReconcileAsync(envelope.WorkflowExecutionId, cancellationToken);
}
