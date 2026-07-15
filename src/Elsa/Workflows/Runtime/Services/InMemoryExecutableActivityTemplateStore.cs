using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Process-local content-addressed store for reusable-activity execution templates.
/// </summary>
public sealed class InMemoryExecutableActivityTemplateStore : IExecutableActivityTemplateStore
{
    private static readonly JsonSerializerOptions ComparisonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, ExecutableActivityTemplate> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExecutableActivityTemplate> _byHash = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

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

    public ValueTask<IReadOnlyCollection<ExecutableActivityTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<ExecutableActivityTemplate>>(_byId.Values.ToArray());
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
