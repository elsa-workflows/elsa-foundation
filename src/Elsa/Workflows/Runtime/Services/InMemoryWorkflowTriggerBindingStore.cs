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
    private readonly HashSet<string> _preparedPublications = new(StringComparer.Ordinal);

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

    public ValueTask PreparePublicationAsync(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePublicationBindings(publicationId, bindings);

        lock (_syncRoot)
        {
            RemoveByPublication(publicationId);
            foreach (var binding in bindings)
            {
                var prepared = binding with { IsActive = false };
                _bindings[prepared.TriggerBindingId] = prepared;
            }
            _preparedPublications.Add(publicationId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByPublicationAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding => StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                .ToArray();
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowTriggerBinding>>(matches);
        }
    }

    public ValueTask ActivatePublicationAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        if (replacedPublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(replacedPublicationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_preparedPublications.Contains(publicationId))
                throw new InvalidOperationException($"Publication '{publicationId}' has no prepared trigger-binding projection.");

            SetPublicationActivity(publicationId, true);
            if (replacedPublicationId is not null && !StringComparer.Ordinal.Equals(replacedPublicationId, publicationId))
                SetPublicationActivity(replacedPublicationId, false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteByPublicationAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            RemoveByPublication(publicationId);
            _preparedPublications.Remove(publicationId);
        }

        return ValueTask.CompletedTask;
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
                    binding.IsActive &&
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

    public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding => binding.IsActive && StringComparer.Ordinal.Equals(binding.StimulusType, stimulusType))
                .ToArray();

            return new ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>>(matches);
        }
    }

    private static void ValidatePublicationBindings(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (!StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                throw new ArgumentException($"Binding '{binding.TriggerBindingId}' does not belong to publication '{publicationId}'.", nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
        }
    }

    private void SetPublicationActivity(string publicationId, bool isActive)
    {
        foreach (var binding in _bindings.Values
                     .Where(binding => StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                     .ToArray())
            _bindings[binding.TriggerBindingId] = binding with { IsActive = isActive };
    }

    private void RemoveByPublication(string publicationId)
    {
        foreach (var bindingId in _bindings.Values
                     .Where(binding => StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                     .Select(binding => binding.TriggerBindingId)
                     .ToArray())
            _bindings.Remove(bindingId);
    }
}
