using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Maps the <see cref="DispatchStimulus"/> API request onto the runtime <see cref="IStimulusRouter"/> and projects
/// its result into a <see cref="DispatchStimulusResponse"/> (W7). The endpoint is the integration surface that
/// makes trigger-based start (E3-1) and cross-execution fan-in (E3-5) reachable from outside the process.
/// </summary>
public sealed class StimulusDispatchService(IStimulusRouter router) : IStimulusDispatchService
{
    private const string RequestedBy = "runtime-api";

    public async Task<DispatchStimulusResponse> DispatchAsync(DispatchStimulus request, CancellationToken cancellationToken)
    {
        var mode = ParseMode(request.Mode);

        var dispatchRequest = new StimulusDispatchRequest(
            stimulusType: request.StimulusType,
            stimulusHash: request.StimulusHash,
            input: request.Input,
            correlationId: NullIfBlank(request.CorrelationId),
            mode: mode,
            idempotencyKey: NullIfBlank(request.IdempotencyKey),
            requestedBy: RequestedBy);

        var result = await router.RouteAsync(dispatchRequest, cancellationToken);
        return DispatchStimulusResponse.From(result);
    }

    private static StimulusRoutingMode ParseMode(string? mode) =>
        mode is null || string.IsNullOrWhiteSpace(mode)
            ? StimulusRoutingMode.StartAndResume
            : Enum.TryParse<StimulusRoutingMode>(mode, ignoreCase: true, out var parsed)
                ? parsed
                : throw new ArgumentException(
                    $"Unknown stimulus routing mode '{mode}'. Valid values are {string.Join(", ", Enum.GetNames<StimulusRoutingMode>())}.",
                    nameof(mode));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>The stimulus dispatch operation the runtime endpoints dispatch to.</summary>
public interface IStimulusDispatchService
{
    Task<DispatchStimulusResponse> DispatchAsync(DispatchStimulus request, CancellationToken cancellationToken);
}
