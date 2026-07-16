using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Default <see cref="IGroundworkRuntimeDocumentUpcasterRegistry"/>. Indexes the contributed
/// <see cref="IGroundworkRuntimeDocumentUpcaster"/> steps per document kind and applies them one
/// version at a time. The chain is validated <b>eagerly at construction</b>, never on first read:
/// a duplicate step, a gap between the lowest and highest registered step of a kind, a step at or
/// beyond a known kind's supported range, or a known kind whose full chain from its minimum-readable
/// version to its current version is incomplete all fail loudly at startup. A document that cannot be brought to the current version
/// must never be deserialized against a mismatched shape.
/// </summary>
public sealed class GroundworkRuntimeDocumentUpcasterRegistry : IGroundworkRuntimeDocumentUpcasterRegistry
{
    private readonly Dictionary<(string DocumentKind, int FromVersion), IGroundworkRuntimeDocumentUpcaster> _steps;

    public GroundworkRuntimeDocumentUpcasterRegistry(IEnumerable<IGroundworkRuntimeDocumentUpcaster> upcasters)
    {
        ArgumentNullException.ThrowIfNull(upcasters);

        _steps = new Dictionary<(string, int), IGroundworkRuntimeDocumentUpcaster>();
        var registeredVersionsByKind = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);

        foreach (var upcaster in upcasters)
        {
            var documentKind = upcaster.DocumentKind;
            ArgumentException.ThrowIfNullOrWhiteSpace(documentKind, $"{nameof(IGroundworkRuntimeDocumentUpcaster)}.{nameof(IGroundworkRuntimeDocumentUpcaster.DocumentKind)}");

            if (upcaster.FromVersion < 1)
            {
                throw new GroundworkRuntimeDocumentVersionException(
                    $"Upcaster '{upcaster.GetType().FullName}' for document kind '{documentKind}' declares FromVersion {upcaster.FromVersion}; " +
                    "version steps start at 1.");
            }

            // A known kind at version N can only be reached from supported historical versions below N.
            // A clean-break kind intentionally has no steps below its minimum-readable floor.
            if (ElsaRuntimeDocumentVersions.All.TryGetValue(documentKind, out var currentVersion))
            {
                var minimumReadableVersion = ElsaRuntimeDocumentVersions.MinimumReadableFor(documentKind);
                if (upcaster.FromVersion < minimumReadableVersion)
                {
                    throw new GroundworkRuntimeDocumentVersionException(
                        $"Upcaster '{upcaster.GetType().FullName}' for document kind '{documentKind}' declares FromVersion {upcaster.FromVersion}, " +
                        $"below the clean-break minimum readable version {minimumReadableVersion}. Compatibility with that retired wire format is not permitted.");
                }

                if (upcaster.FromVersion >= currentVersion)
                {
                    throw new GroundworkRuntimeDocumentVersionException(
                        $"Upcaster '{upcaster.GetType().FullName}' for document kind '{documentKind}' declares FromVersion {upcaster.FromVersion}, " +
                        $"but the current version of that kind is {currentVersion}. Upcasters may only move a supported version below the current one forward; " +
                        "bump the kind's current version in ElsaRuntimeDocumentVersions when adding a step.");
                }
            }

            var key = (documentKind, upcaster.FromVersion);
            if (!_steps.TryAdd(key, upcaster))
            {
                throw new GroundworkRuntimeDocumentVersionException(
                    $"Multiple upcasters are registered for document kind '{documentKind}' from version {upcaster.FromVersion}: " +
                    $"'{_steps[key].GetType().FullName}' and '{upcaster.GetType().FullName}'. Each version step must have exactly one upcaster.");
            }

            if (!registeredVersionsByKind.TryGetValue(documentKind, out var versions))
            {
                versions = new SortedSet<int>();
                registeredVersionsByKind[documentKind] = versions;
            }

            versions.Add(upcaster.FromVersion);
        }

        ValidateNoGapsBetweenRegisteredSteps(registeredVersionsByKind);
        ValidateKnownKindChainsReachCurrent();
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
            // Eager construction-time validation guarantees this step exists for known kinds; the guard
            // stays as a defensive backstop so a malformed chain can never silently skip a version.
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

    // No gap may exist between the lowest and highest registered step of a kind: a document at any
    // covered version must be able to walk every intermediate step without hitting a missing one.
    private void ValidateNoGapsBetweenRegisteredSteps(Dictionary<string, SortedSet<int>> registeredVersionsByKind)
    {
        foreach (var (documentKind, versions) in registeredVersionsByKind)
        {
            for (var version = versions.Min; version < versions.Max; version++)
            {
                if (!_steps.ContainsKey((documentKind, version)))
                {
                    throw new GroundworkRuntimeDocumentVersionException(
                        $"The upcaster chain for document kind '{documentKind}' has a gap: no upcaster is registered from version {version} to {version + 1}, " +
                        $"but steps exist on both sides (versions {versions.Min} through {versions.Max}). " +
                        $"Register an {nameof(IGroundworkRuntimeDocumentUpcaster)} for the missing step.");
                }
            }
        }
    }

    // Every known kind must have a contiguous chain from its minimum readable version to its current
    // version. Clean-break versions below that floor intentionally have no upcaster.
    private void ValidateKnownKindChainsReachCurrent()
    {
        foreach (var (documentKind, currentVersion) in ElsaRuntimeDocumentVersions.All)
        {
            var minimumReadableVersion = ElsaRuntimeDocumentVersions.MinimumReadableFor(documentKind);
            for (var version = minimumReadableVersion; version < currentVersion; version++)
            {
                if (!_steps.ContainsKey((documentKind, version)))
                {
                    throw new GroundworkRuntimeDocumentVersionException(
                        $"Document kind '{documentKind}' is at version {currentVersion} but has no upcaster from version {version} to {version + 1}. " +
                        $"Every version step from the minimum readable version {minimumReadableVersion} to the current version must have exactly one upcaster. " +
                        $"Register an {nameof(IGroundworkRuntimeDocumentUpcaster)} for the missing step.");
                }
            }
        }
    }
}
