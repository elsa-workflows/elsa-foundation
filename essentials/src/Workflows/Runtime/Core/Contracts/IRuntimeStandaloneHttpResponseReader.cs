using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>The selected durable source's committed response state.</summary>
public enum RuntimeStandaloneHttpResponseState { Legacy, Pending, Missing, TooLarge, Ready }

/// <summary>One exact observed response for an already authorized HTTP source.</summary>
public sealed record RuntimeStandaloneHttpResponse(RuntimeStandaloneHttpResponseState State, string? ActorReference = null,
    int StatusCode = 0, string? ContentType = null, JsonElement? Body = null, IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>Reads the response for a source the HTTP transport has already authorized.</summary>
public interface IRuntimeStandaloneHttpResponseReader
{
    ValueTask<RuntimeStandaloneHttpResponse> ReadAsync(TriggerScope scope, string sourceOutboxItemId, CancellationToken cancellationToken);
}
