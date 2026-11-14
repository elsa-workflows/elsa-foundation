using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.StandaloneTriggers;

/// <summary>Module-owned producer identity and detached HTTP payload schema.</summary>
public sealed class StandaloneHttpTriggerSource
{
    public static StandaloneTriggerSourceDescriptor Descriptor { get; } = new("http", 1, "Http",
        new ValueTypeDescriptor("Elsa.Http.Request", schemaVersion: 1));
}

/// <summary>The middleware binds only an actual persisted route and the authenticated source actor in this request scope.</summary>
internal sealed class HttpTriggerSourceContext(IHttpContextAccessor http, IPersistenceAccessContextAccessor persistence,
    NormalizedPrincipalValidator? validator = null) : IStandaloneTriggerSourceContext<StandaloneHttpTriggerSource>
{
    public bool SupportsResourceAuthorization => true;
    private StandaloneTriggerInvocationContext? current;
    private HttpContext? request;
    internal void Clear() { current = null; request = null; }
    internal void Bind(RuntimeIngressReservation reservation)
    {
        if (current is not null || http.HttpContext is not { } context)
            throw new TriggerOperationException("unavailable", "HTTP source context cannot be rebound or detached from its request.");
        persistence.Current.EnsureScope(reservation.Scope.Persistence);
        string rootActor;
        if (validator is not null && validator.TryGetNormalizedPrincipal(context.User, out var principal))
        {
            var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).ToArray();
            var subjects = principal.FindAll(IdentityClaimTypes.UserId).Select(claim => claim.Value).ToArray();
            if (tenants.Length > 1 || tenants.Any(string.IsNullOrWhiteSpace) || tenants.SingleOrDefault() != reservation.Scope.TenantId ||
                subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]))
                throw new TriggerOperationException("forbidden", "This source request does not belong to the reserved tenant.");
            rootActor = subjects[0];
        }
        else
        {
            if (reservation.Metadata.GetValueOrDefault(Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey) != bool.FalseString)
                throw new TriggerOperationException("unauthorized", "Sign in to invoke this HTTP trigger.");
            rootActor = "standalone-http:anonymous";
        }
        request = context;
        current = new(reservation.Scope, new WorkflowExecutionPartition(reservation.Scope.Persistence.Value),
            new WorkflowExecutionAuthoritySnapshot("standalone-trigger", rootActor)) { Reservation = reservation };
    }
    internal void CaptureRequest(HttpRequestModel model, string stimulusHash, IReadOnlyList<RuntimeIngressWaitObservation>? observedWaits = null)
    {
        if (current is null || current.WaitCandidate is not null || request is null || !ReferenceEquals(request, http.HttpContext))
            throw new TriggerOperationException("unavailable", "HTTP request intent cannot be rebound or detached from its source.");
        // Template-derived RouteData belongs to the accepted capture, never the submitted-intent identity.
        var intent = JsonSerializer.SerializeToElement(new { method = request.Request.Method.ToUpperInvariant(),
            path = request.Request.Path.Value, query = model.Query,
            headers = model.Headers.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value, StringComparer.Ordinal),
            body = model.Body, contentType = request.Request.ContentType });
        var candidate = new RuntimeIngressWaitCandidate(HttpEndpointStimulus.StimulusType, stimulusHash,
            JsonSerializer.SerializeToElement(model), new Dictionary<string, string>(),
            new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(HttpRequestModel)), schemaVersion: 1), HttpEndpoint.ActivityType, observedWaits);
        current = current with { SubmittedIntent = intent, WaitCandidate = candidate };
    }
    public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return current is not null && ReferenceEquals(request, http.HttpContext) ? ValueTask.FromResult(current) :
            ValueTask.FromException<StandaloneTriggerInvocationContext>(new TriggerOperationException("unavailable", "No current trusted HTTP source context is bound."));
    }
    public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reservation = current?.Reservation;
        return ValueTask.FromResult(current?.WaitCandidate?.ObservedWaits is not null && request is not null && !request.RequestAborted.IsCancellationRequested && ReferenceEquals(request, http.HttpContext) &&
            reservation is not null && reservation.OwnerKind == RuntimeIngressReservation.StandaloneTriggerOwner && reservation.OwnerId == trigger.Id &&
            trigger.Id == signal.TriggerId && trigger.TypeKey == StandaloneHttpTriggerSource.Descriptor.TypeKey &&
            trigger.TenantId == reservation.Scope.TenantId && current!.Scope == reservation.Scope && persistence.Current.Scope == reservation.Scope.Persistence);
    }
}
