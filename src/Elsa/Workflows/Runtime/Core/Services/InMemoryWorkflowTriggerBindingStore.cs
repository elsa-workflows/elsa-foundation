using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// In-memory <see cref="IWorkflowTriggerBindingStore"/>, the default before a durable provider is
/// composed in. Suitable for tests and single-process hosts; a restart loses the trigger index, which
/// is why production hosts swap in the Groundwork-backed store.
/// </summary>
public sealed class InMemoryWorkflowTriggerBindingStore : IWorkflowTriggerBindingStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, WorkflowTriggerBinding> _bindings = new(StringComparer.Ordinal);

    public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.TriggerBindingId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _bindings[binding.TriggerBindingId] = binding;
            return new ValueTask<WorkflowTriggerBinding>(binding);
        }
    }

    public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var doomed = _bindings.Values
                .Where(binding => StringComparer.Ordinal.Equals(binding.ArtifactId, artifactId))
                .Select(binding => binding.TriggerBindingId)
                .ToArray();

            foreach (var id in doomed)
                _bindings.Remove(id);

            return new ValueTask<int>(doomed.Length);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding =>
                    StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType) &&
                    StringComparer.Ordinal.Equals(binding.StimulusHash, stimulusHash))
                .ToArray();

            return new ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>>(matches);
        }
    }

    public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding => StringComparer.Ordinal.Equals(binding.ArtifactId, artifactId))
                .ToArray();

            return new ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>>(matches);
        }
    }
}
