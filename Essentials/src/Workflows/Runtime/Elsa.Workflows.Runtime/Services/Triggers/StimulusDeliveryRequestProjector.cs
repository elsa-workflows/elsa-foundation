using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Uses the owning decoder for canonical persisted input, shared by routing and direct saved-start admission.</summary>
internal sealed class StimulusDeliveryRequestProjector
{
    private readonly IReadOnlyDictionary<string, IStimulusDeliveryIntentAdapter> _adapters;

    public StimulusDeliveryRequestProjector(IEnumerable<IStimulusDeliveryIntentAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var owners = new Dictionary<string, IStimulusDeliveryIntentAdapter>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            ArgumentException.ThrowIfNullOrWhiteSpace(adapter.IntentKind);
            if (!owners.TryAdd(adapter.IntentKind, adapter))
                throw new InvalidOperationException("Several adapters own the same stimulus intent kind.");
        }
        _adapters = owners;
    }

    public bool IsConfigured => _adapters.Count != 0;
    public bool Owns(string kind) => _adapters.ContainsKey(kind);

    public StimulusDispatchRequest Read(PostCommitDeliveryContext source)
    {
        if (!_adapters.TryGetValue(source.Intent.Kind, out var adapter))
            throw new NotSupportedException("No owning adapter can decode the canonical stimulus intent.");
        var request = adapter.CreateRequest(source.Intent) ?? throw new InvalidDataException("The owning stimulus adapter returned no request.");
        if (!Enum.IsDefined(request.Mode) || request.IdempotencyKey != source.Intent.IdempotencyKey || request.IdempotencyKey is null ||
            request.StartKeyScope != StimulusStartKeyScope.Artifact || request.MatchedTriggerBindings is not null || request.DispatchOptions is not null ||
            !request.ResumeAllMatchingBookmarks || request.TargetWorkflowExecutionId is { } target && target != source.Intent.WorkflowExecutionId)
            throw new InvalidDataException("The stimulus adapter changed the immutable source key or declared an unsupported routing override.");
        return request;
    }
}
