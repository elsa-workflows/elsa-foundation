using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Process-local content-addressed store for reusable-activity execution templates.
/// </summary>
public sealed class InMemoryExecutableActivityTemplateStore : IExecutableActivityTemplateStore
{
    private static readonly JsonSerializerOptions ComparisonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, ExecutableActivityTemplate> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExecutableActivityTemplate> _byHash = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly InMemoryWorkflowExecutableStore? _executables;
    private readonly InMemoryWorkflowExecutableSourceReferenceStore? _references;
    private readonly TimeProvider _clock = TimeProvider.System;

    public InMemoryExecutableActivityTemplateStore() { }

    private InMemoryExecutableActivityTemplateStore(IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, TimeProvider? clock = null)
    {
        _executables = executables as InMemoryWorkflowExecutableStore;
        _references = references as InMemoryWorkflowExecutableSourceReferenceStore;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Explicitly binds native maintenance without changing ordinary singleton DI construction.</summary>
    public static InMemoryExecutableActivityTemplateStore CreateForMaintenance(IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(executables); ArgumentNullException.ThrowIfNull(references);
        return new(executables, references, clock);
    }

    public bool CanMaintainReferences(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references) =>
        _executables is not null && _references is not null && ReferenceEquals(executables, _executables) &&
        ReferenceEquals(references, _references) && ReferenceEquals(_references.FencedArtifactStore, _executables);

    public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(template);

        lock (_gate)
        {
            if (_byId.TryGetValue(template.TemplateId, out var existingById))
            {
                EnsureSameIdentityAndContent(existingById, template);
                return ValueTask.CompletedTask;
            }

            if (_byHash.TryGetValue(template.TemplateHash, out var existingByHash))
            {
                throw new InvalidOperationException(
                    $"Template hash '{template.TemplateHash}' is already bound to id '{existingByHash.TemplateId}', not '{template.TemplateId}'.");
            }

            _byId.Add(template.TemplateId, template);
            _byHash.Add(template.TemplateHash, template);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template); ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceReferenceId);
        cancellationToken.ThrowIfCancellationRequested(); RequireMaintenance();
        if (reference.ArtifactId != template.TemplateId)
            throw new ArgumentException("The source reference must name this template.", nameof(reference));
        return ValueTask.FromResult(_executables!.ExecuteUnderMaterialGate(_ =>
        {
            lock (_gate)
                return _references!.ExecuteUnderReferenceGate(references =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = _clock.GetUtcNow();
                    if (!reference.IsLive(now)) throw new InvalidOperationException("The proposed template source reference is not live.");
                    var winner = references.GetValueOrDefault(reference.SourceReferenceId);
                    if (winner is not null && (!winner.IsLive(now) || !WorkflowExecutableSourceReferenceComparer.SameIdentity(winner,
                            reference with { CreatedAt = winner.CreatedAt, PublishedAt = winner.PublishedAt })))
                        throw new InvalidOperationException("The template source reference conflicts or is no longer live.");
                    // Validate before either write. These are synchronous mutations of the actual selected stores.
                    ValidateDependencies(template);
                    SaveAsync(template, cancellationToken).GetAwaiter().GetResult();
                    if (winner is null) _references.SaveAsync(reference, cancellationToken).GetAwaiter().GetResult();
                    return winner ?? reference;
                });
        }));
    }

    public ValueTask<bool> TryDeleteDoomedReferenceAsync(WorkflowExecutableSourceReference expectedReference,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedReference);
        cancellationToken.ThrowIfCancellationRequested(); RequireMaintenance();
        return ValueTask.FromResult(_executables!.ExecuteUnderMaterialGate(executables =>
        {
            lock (_gate)
                return _references!.ExecuteUnderReferenceGate(_ =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_byId.TryGetValue(expectedReference.ArtifactId, out var template) || IsReferenced(template, executables)) return false;
                    return _references.TryDeleteDoomedAsync(expectedReference, now, cancellationToken).GetAwaiter().GetResult();
                });
        }));
    }

    public ValueTask<bool> TryDeleteUnreferencedAsync(ExecutableActivityTemplate expectedTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedTemplate);
        cancellationToken.ThrowIfCancellationRequested(); RequireMaintenance();
        return ValueTask.FromResult(_executables!.ExecuteUnderMaterialGate(executables =>
        {
            lock (_gate)
                return _references!.ExecuteUnderReferenceGate(references =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_byId.TryGetValue(expectedTemplate.TemplateId, out var current) || !ReferenceEquals(current, expectedTemplate) ||
                        IsReferenced(current, executables) || references.Values.Any(reference => reference.ArtifactId == current.TemplateId)) return false;
                    return DeleteAsync(current.TemplateId, cancellationToken).GetAwaiter().GetResult();
                });
        }));
    }

    private void RequireMaintenance()
    {
        if (_executables is null || _references is null || !CanMaintainReferences(_executables, _references))
            throw new NotSupportedException("Template maintenance requires the exact native artifact and reference stores.");
    }

    private void ValidateDependencies(ExecutableActivityTemplate template)
    {
        foreach (var identity in template.ClosedTemplates.Concat(template.DirectDependencies.Select(dependency =>
                     new ExecutableActivityTemplateIdentity(dependency.TemplateId, dependency.TemplateHash))))
        {
            if (identity.TemplateId == template.TemplateId && identity.TemplateHash == template.TemplateHash) continue;
            if (!_byId.TryGetValue(identity.TemplateId, out var dependency) || dependency.TemplateHash != identity.TemplateHash)
                throw new InvalidDataException($"Activity template dependency '{identity.TemplateId}' is missing or has a different hash.");
        }
    }

    private bool IsReferenced(ExecutableActivityTemplate candidate, IReadOnlyCollection<WorkflowExecutable> executables) =>
        executables.Any(executable => NamesHash(executable.CompatibilityMetadata, candidate.TemplateHash) ||
            executable.NodesById.Values.Any(node => NamesHash(node.Metadata, candidate.TemplateHash))) ||
        _byId.Values.Any(parent => parent.TemplateId != candidate.TemplateId &&
            (parent.ClosedTemplates.Any(dependency => dependency.TemplateId == candidate.TemplateId || dependency.TemplateHash == candidate.TemplateHash) ||
             parent.DirectDependencies.Any(dependency => dependency.TemplateId == candidate.TemplateId || dependency.TemplateHash == candidate.TemplateHash) ||
             NamesHash(parent.CompatibilityMetadata, candidate.TemplateHash) || parent.NodesById.Values.Any(node => NamesHash(node.Metadata, candidate.TemplateHash))));

    private static bool NamesHash(IReadOnlyDictionary<string, string> metadata, string hash) =>
        metadata.TryGetValue("activity.templateHash", out var value) && StringComparer.Ordinal.Equals(value, hash);

    public ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        lock (_gate)
            return ValueTask.FromResult(_byId.GetValueOrDefault(templateId));
    }

    public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);

        lock (_gate)
            return ValueTask.FromResult(_byHash.GetValueOrDefault(templateHash));
    }

    public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            const string queryBinding = "executable-activity-template:list";
            var lastId = InMemoryRuntimeStoreContinuation.Decode(request.ContinuationToken, queryBinding, nameof(request.ContinuationToken));
            var ordered = _byId.Values
                .OrderBy(template => template.TemplateId, StringComparer.Ordinal)
                .Where(template => lastId is null || StringComparer.Ordinal.Compare(template.TemplateId, lastId) > 0)
                .Take(request.Limit + 1)
                .ToArray();
            var items = ordered.Take(request.Limit).ToArray();
            var nextContinuation = ordered.Length > request.Limit
                ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, items[^1].TemplateId)
                : null;
            return ValueTask.FromResult(new RuntimeStorePage<ExecutableActivityTemplate>(request, items, nextContinuation));
        }
    }

    public ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        lock (_gate)
        {
            if (!_byId.Remove(templateId, out var template))
                return ValueTask.FromResult(false);
            _byHash.Remove(template.TemplateHash);
            return ValueTask.FromResult(true);
        }
    }

    private static void EnsureSameIdentityAndContent(ExecutableActivityTemplate existing, ExecutableActivityTemplate candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.TemplateHash, candidate.TemplateHash))
        {
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' is already bound to hash '{existing.TemplateHash}', not '{candidate.TemplateHash}'.");
        }

        var existingJson = ComparableContent(existing);
        var candidateJson = ComparableContent(candidate);
        if (!JsonNode.DeepEquals(existingJson, candidateJson))
        {
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
        }
    }

    private static JsonNode? ComparableContent(ExecutableActivityTemplate template)
    {
        var json = JsonSerializer.SerializeToNode(template, ComparisonOptions);
        if (json is JsonObject content)
            content.Remove("createdAt");
        return json;
    }
}
