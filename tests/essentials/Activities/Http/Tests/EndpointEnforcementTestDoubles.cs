using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// An <see cref="IStimulusRouter"/> whose <see cref="RouteAsync"/> can delay (respecting the passed
/// cancellation token) or throw, so the middleware's per-endpoint timeout + fault-mapping paths (spec 089 C,
/// FR-013/FR-014) can be exercised. The plain recording router (<c>RecordingStimulusRouter</c>) only returns a
/// fixed outcome; these behaviours are what the fault tests need on top of that.
/// </summary>
internal sealed class BehaviourStimulusRouter : IStimulusRouter
{
    private readonly Func<StimulusDispatchRequest, CancellationToken, ValueTask<StimulusRoutingResult>> _behaviour;

    private BehaviourStimulusRouter(Func<StimulusDispatchRequest, CancellationToken, ValueTask<StimulusRoutingResult>> behaviour) =>
        _behaviour = behaviour;

    /// <summary>The requests routed through this fake, in call order.</summary>
    public List<StimulusDispatchRequest> Requests { get; } = new();

    /// <summary>Delays (honouring the cancellation token) longer than the endpoint timeout, so the linked CTS trips.</summary>
    public static BehaviourStimulusRouter Delaying(TimeSpan delay) =>
        new(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            return Empty();
        });

    /// <summary>Throws the given exception synchronously from the dispatch.</summary>
    public static BehaviourStimulusRouter Throwing(Exception exception) =>
        new((_, _) => throw exception);

    public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return _behaviour(request, cancellationToken);
    }

    private static StimulusRoutingResult Empty() =>
        new(Array.Empty<StimulusStartOutcome>(), Array.Empty<StimulusResumeOutcome>());
}

/// <summary>
/// A fake <see cref="IHttpEndpointAuthorizationHandler"/> that returns a configured result — or throws a
/// configured exception (e.g. an <c>HttpEndpointAuthorizationConfigurationException</c> to exercise the 500 path,
/// #592 item 11) — and records the <see cref="AuthorizeHttpEndpointContext"/> it was handed (so tests can assert
/// the policy string flowed from binding metadata).
/// </summary>
internal sealed class FakeAuthorizationHandler : IHttpEndpointAuthorizationHandler
{
    private readonly bool _authorize;
    private readonly Exception? _throws;
    private readonly Func<string?, bool>? _decidePerPolicy;

    public FakeAuthorizationHandler(bool authorize) => _authorize = authorize;

    private FakeAuthorizationHandler(Exception throws) => _throws = throws;

    private FakeAuthorizationHandler(Func<string?, bool> decidePerPolicy) => _decidePerPolicy = decidePerPolicy;

    /// <summary>A handler that throws <paramref name="exception"/> instead of returning a decision.</summary>
    public static FakeAuthorizationHandler Throwing(Exception exception) => new(exception);

    /// <summary>
    /// A handler whose decision depends on the policy being evaluated — used to prove that when several distinct
    /// policies are merged from divergent waiting bookmarks (D-D5), EVERY one is evaluated and any single failure
    /// denies the request.
    /// </summary>
    public static FakeAuthorizationHandler PerPolicy(Func<string?, bool> decide) => new(decide);

    /// <summary>The most recent context (retained for existing single-policy assertions).</summary>
    public AuthorizeHttpEndpointContext? LastContext { get; private set; }

    /// <summary>Every context handed to the handler, in call order (one per evaluated policy).</summary>
    public List<AuthorizeHttpEndpointContext> Contexts { get; } = new();

    public bool WasInvoked => LastContext is not null;

    /// <summary>The distinct, non-null policy strings the handler was asked to evaluate.</summary>
    public IReadOnlyCollection<string> EvaluatedPolicies =>
        Contexts.Select(context => context.Policy).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    public ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
    {
        LastContext = context;
        Contexts.Add(context);
        if (_throws is not null)
            throw _throws;
        return ValueTask.FromResult(_decidePerPolicy?.Invoke(context.Policy) ?? _authorize);
    }
}

/// <summary>
/// A fake <see cref="IHttpEndpointFaultHandler"/> that records the fault context and writes a sentinel status,
/// so a test can prove the seam handler was invoked <em>instead of</em> the middleware's inline fallback mapping.
/// </summary>
internal sealed class FakeFaultHandler(int statusCode) : IHttpEndpointFaultHandler
{
    public HttpEndpointFaultContext? LastContext { get; private set; }

    public bool WasInvoked => LastContext is not null;

    public ValueTask HandleAsync(HttpEndpointFaultContext context)
    {
        LastContext = context;
        context.HttpContext.Response.StatusCode = statusCode;
        return ValueTask.CompletedTask;
    }
}
