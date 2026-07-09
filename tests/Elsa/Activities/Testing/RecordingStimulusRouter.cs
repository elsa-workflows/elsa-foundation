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

    /// <summary>Routes nothing (no matches). This is the constructor DI activates.</summary>
    public RecordingStimulusRouter() => _starts = Array.Empty<StimulusStartOutcome>();

    /// <summary>Routes the given start outcomes on every call.</summary>
    public RecordingStimulusRouter(params StimulusStartOutcome[] starts) => _starts = starts;

    /// <summary>The requests routed through this fake, in call order.</summary>
    public List<StimulusDispatchRequest> Requests { get; } = new();

    /// <summary>Whether <see cref="RouteAsync"/> has been called at least once.</summary>
    public bool WasInvoked => Requests.Count > 0;

    public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return ValueTask.FromResult(new StimulusRoutingResult(_starts, Array.Empty<StimulusResumeOutcome>()));
    }
}
