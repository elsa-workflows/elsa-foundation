using System.Security.Cryptography;
using System.Text;
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
        WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);
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

    public ValueTask<WorkflowTriggerBindingPage> ListByPublicationAsync(
        WorkflowTriggerBindingPublicationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding => StringComparer.Ordinal.Equals(binding.PublicationId, query.PublicationId))
                .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(CreatePage(query, matches));
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

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
        WorkflowTriggerBindingPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding =>
                    binding.IsActive &&
                    StringComparer.Ordinal.Equals(binding.StimulusType, query.StimulusType) &&
                    StringComparer.Ordinal.Equals(binding.StimulusHash, query.StimulusHash))
                .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(CreatePage(query, matches));
        }
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
        WorkflowTriggerBindingArtifactPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding => StringComparer.Ordinal.Equals(binding.ArtifactId, query.ArtifactId))
                .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult(CreatePage(query, matches));
        }
    }

    public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
        WorkflowTriggerBindingTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var matches = _bindings.Values
                .Where(binding =>
                    binding.IsActive &&
                    StringComparer.Ordinal.Equals(binding.StimulusType, query.StimulusType))
                .OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
                .ToArray();
            return ValueTask.FromResult(CreatePage(query, matches));
        }
    }

    private static void ValidatePublicationBindings(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);
            if (!StringComparer.Ordinal.Equals(binding.PublicationId, publicationId))
                throw new ArgumentException($"Binding '{binding.TriggerBindingId}' does not belong to publication '{publicationId}'.", nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
        }
    }

    private static WorkflowTriggerBindingPage CreatePage(
        WorkflowTriggerBindingPageRequest query,
        IReadOnlyList<WorkflowTriggerBinding> matches)
    {
        var continuationId = DecodeContinuation(query);
        var remaining = continuationId is null
            ? matches
            : matches
                .Where(binding => StringComparer.Ordinal.Compare(binding.TriggerBindingId, continuationId) > 0)
                .ToArray();
        var page = remaining.Take(query.Limit).ToArray();
        var nextContinuation = remaining.Count > page.Length
            ? EncodeContinuation(query, page[^1].TriggerBindingId)
            : null;
        return new WorkflowTriggerBindingPage(query, page, matches.Count, nextContinuation);
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

    private static string EncodeContinuation(WorkflowTriggerBindingPageRequest query, string lastBindingId)
    {
        var payload = Encoding.UTF8.GetBytes($"{QueryBinding(query)}\0{lastBindingId}");
        var checksum = SHA256.HashData(payload);
        return $"imq1.{Base64UrlEncode(payload)}.{Base64UrlEncode(checksum)}";
    }

    private static string? DecodeContinuation(WorkflowTriggerBindingPageRequest query)
    {
        if (query.ContinuationToken is null)
            return null;

        try
        {
            var parts = query.ContinuationToken.Split('.');
            if (parts is not ["imq1", var encodedPayload, var encodedChecksum])
                throw new FormatException();

            var payload = Base64UrlDecode(encodedPayload);
            var suppliedChecksum = Base64UrlDecode(encodedChecksum);
            var expectedChecksum = SHA256.HashData(payload);
            if (suppliedChecksum.Length != expectedChecksum.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedChecksum, expectedChecksum))
            {
                throw new FormatException();
            }

            var decoded = Encoding.UTF8.GetString(payload);
            var separator = decoded.IndexOf('\0');
            if (separator <= 0 ||
                separator == decoded.Length - 1 ||
                !StringComparer.Ordinal.Equals(decoded[..separator], QueryBinding(query)))
            {
                throw new FormatException();
            }

            return decoded[(separator + 1)..];
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                "The trigger-binding continuation token is invalid or belongs to another trigger-binding query.",
                nameof(query),
                exception);
        }
    }

    private static string QueryBinding(WorkflowTriggerBindingPageRequest query)
    {
        var value = query switch
        {
            WorkflowTriggerBindingPageQuery exact =>
                $"exact\0{exact.StimulusType}\0{exact.StimulusHash}",
            WorkflowTriggerBindingTypePageQuery byType =>
                $"type\0{byType.StimulusType}",
            WorkflowTriggerBindingPublicationPageQuery byPublication =>
                $"publication\0{byPublication.PublicationId}",
            WorkflowTriggerBindingArtifactPageQuery byArtifact =>
                $"artifact\0{byArtifact.ArtifactId}",
            _ => throw new ArgumentOutOfRangeException(nameof(query), query, "Unsupported trigger-binding page request.")
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
