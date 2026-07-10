using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>
/// Configurable recording <see cref="IStimulusRouter"/> fake for activity tests. Each <see cref="RouteAsync"/> call
/// records the incoming <see cref="StimulusDispatchRequest"/> and returns the start outcomes supplied at construction
/// (a parameterless instance routes nothing, i.e. no matches).
/// </summary>
public sealed class RecordingStimulusRouter : IStimulusRouter
{
    private readonly StimulusStartOutcome[] _starts;
    private readonly StimulusResumeOutcome[] _resumes;
    private readonly Func<StimulusDispatchRequest, ValueTask>? _onRoute;

    /// <summary>Routes nothing (no matches). This is the constructor DI activates.</summary>
    public RecordingStimulusRouter()
    {
        _starts = Array.Empty<StimulusStartOutcome>();
        _resumes = Array.Empty<StimulusResumeOutcome>();
    }

    /// <summary>Routes the given start outcomes (and no resumes) on every call.</summary>
    public RecordingStimulusRouter(params StimulusStartOutcome[] starts)
    {
        _starts = starts;
        _resumes = Array.Empty<StimulusResumeOutcome>();
    }

    private RecordingStimulusRouter(
        StimulusStartOutcome[] starts,
        StimulusResumeOutcome[] resumes,
        Func<StimulusDispatchRequest, ValueTask>? onRoute)
    {
        _starts = starts;
        _resumes = resumes;
        _onRoute = onRoute;
    }

    /// <summary>Routes the given start AND resume outcomes on every call (spec 089 D StartAndResume).</summary>
    public static RecordingStimulusRouter WithOutcomes(
        IEnumerable<StimulusStartOutcome>? starts = null,
        IEnumerable<StimulusResumeOutcome>? resumes = null) =>
        new(starts?.ToArray() ?? Array.Empty<StimulusStartOutcome>(), resumes?.ToArray() ?? Array.Empty<StimulusResumeOutcome>(), null);

    /// <summary>
    /// Routes the given outcomes but first invokes <paramref name="onRoute"/> during <see cref="RouteAsync"/>, so a
    /// test can simulate the inline drain writing the live response / marking the sync sink before the router returns
    /// (spec 089 E, the synchronous same-exchange path).
    /// </summary>
    public static RecordingStimulusRouter WithCallback(
        Func<StimulusDispatchRequest, ValueTask> onRoute,
        IEnumerable<StimulusStartOutcome>? starts = null,
        IEnumerable<StimulusResumeOutcome>? resumes = null) =>
        new(starts?.ToArray() ?? Array.Empty<StimulusStartOutcome>(), resumes?.ToArray() ?? Array.Empty<StimulusResumeOutcome>(), onRoute);

    /// <summary>The requests routed through this fake, in call order.</summary>
    public List<StimulusDispatchRequest> Requests { get; } = new();

    /// <summary>Whether <see cref="RouteAsync"/> has been called at least once.</summary>
    public bool WasInvoked => Requests.Count > 0;

    public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_onRoute is not null)
            await _onRoute(request);
        return new StimulusRoutingResult(_starts, _resumes);
    }
}
