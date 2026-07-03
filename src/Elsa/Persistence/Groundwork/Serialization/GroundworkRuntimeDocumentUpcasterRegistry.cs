using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Default <see cref="IGroundworkRuntimeDocumentUpcasterRegistry"/>. Indexes the contributed
/// <see cref="IGroundworkRuntimeDocumentUpcaster"/> steps per document kind and applies them one
/// version at a time. Duplicate steps and chain gaps fail loudly: a document that cannot be brought
/// to the target version must never be deserialized against a mismatched shape.
/// </summary>
public sealed class GroundworkRuntimeDocumentUpcasterRegistry : IGroundworkRuntimeDocumentUpcasterRegistry
{
    private readonly Dictionary<(string DocumentKind, int FromVersion), IGroundworkRuntimeDocumentUpcaster> _steps;

    public GroundworkRuntimeDocumentUpcasterRegistry(IEnumerable<IGroundworkRuntimeDocumentUpcaster> upcasters)
    {
        _steps = new Dictionary<(string, int), IGroundworkRuntimeDocumentUpcaster>();

        foreach (var upcaster in upcasters)
        {
            var key = (upcaster.DocumentKind, upcaster.FromVersion);
            if (!_steps.TryAdd(key, upcaster))
            {
                throw new GroundworkRuntimeDocumentVersionException(
                    $"Multiple upcasters are registered for document kind '{upcaster.DocumentKind}' from version {upcaster.FromVersion}: " +
                    $"'{_steps[key].GetType().FullName}' and '{upcaster.GetType().FullName}'. Each version step must have exactly one upcaster.");
            }
        }
    }

    public JsonObject Upcast(string documentKind, int fromVersion, int toVersion, JsonObject content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentNullException.ThrowIfNull(content);

        if (fromVersion > toVersion)
        {
            throw new GroundworkRuntimeDocumentVersionException(
                $"Cannot upcast document kind '{documentKind}' from version {fromVersion} down to {toVersion}; upcasting only moves forward.");
        }

        var current = content;
        for (var version = fromVersion; version < toVersion; version++)
        {
            if (!_steps.TryGetValue((documentKind, version), out var step))
            {
                throw new GroundworkRuntimeDocumentVersionException(
                    $"No upcaster is registered for document kind '{documentKind}' from version {version} to {version + 1} " +
                    $"(needed to upcast a version {fromVersion} document to version {toVersion}). " +
                    $"Register an {nameof(IGroundworkRuntimeDocumentUpcaster)} for this step; the persisted document cannot be read otherwise.");
            }

            current = step.Upcast(current)
                      ?? throw new GroundworkRuntimeDocumentVersionException(
                          $"Upcaster '{step.GetType().FullName}' returned null content for document kind '{documentKind}' at version {version}.");
        }

        return current;
    }
}
