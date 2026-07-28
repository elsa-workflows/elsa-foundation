using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>
/// Model X reconciliation policy (Unit C 2026-05-28; pending 2026-06-01 architecture
/// review). For each candidate version contributed by source modules:
///
/// 1. Find the parent definition by its surrogate Id or by the natural key
///    <c>(SourceKind, SourceId, ActivityTypeKey)</c>.
/// 2. If no parent exists, persist a fresh Definition + Version with immutable provenance.
///    The version carries an immutable <see cref="ActivityDefinitionVersion.ReconcilliationHash"/>
///    computed from its content.
/// 3. If the parent exists but the specific Version number does not, append the Version
///    with immutable provenance (including ProvisioningHash).
/// 4. If <c>(DefinitionId, Version)</c> is already persisted, compute the incoming hash
///    and compare to the persisted ProvisioningHash:
///    - Any hash difference → <see cref="ActivityVersionHashMismatchException"/> (the source
///      is broken — same logical identity, different content).
///    - Hashes match → skip or throw per the <see cref="DuplicateHandling"/> option.
///
/// No per-pass mutating state is carried by any entity in this domain. Source disappearance
/// is intentionally not tracked. Versions are never deleted.
///
/// Reads are batched (issue #417 item 1): the definitions and versions the whole candidate set could
/// touch are prefetched once, then the per-candidate lookups run against in-memory indexes that are
/// updated on every create/append. This is byte-for-byte result-equivalent to a per-candidate re-read
/// — including two candidates that target the same not-yet-persisted definition, where the first
/// creates it and the second must observe it — because the indexes mirror post-write state exactly.
/// All throws (identity-collision, hash-mismatch) still fire lazily inside the ordered candidate loop
/// at the same candidate they did before batching; the prefetch is read-only and never throws.
/// </summary>
public sealed class ActivityVersionReconciler(
    ILogger<ActivityVersionReconciler> logger,
    IActivityDefinitionHasher hasher,
    IInlineEventPublisher eventPublisher,
    IOptions<ActivityVersionReconcilerOptions> options,
    IActivityDefinitionStore definitionStore,
    IActivityDefinitionVersionStore versionStore,
    IAddActivityDefinitionCommand addNewDefinitionCommand,
    IAddActivityDefinitionVersionCommand addVersionCommand,
    IEnumerable<IActivitySourceVersionPublisher>? sourceVersionPublishers = null
)
    : IActivityVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Handlers contribute by adding to the event's directly-accessible Versions collection;
        // the reconciler reads the accumulated set after dispatch.
        var @event = new OnActivityVersionsReconciling();
        await eventPublisher.Publish(@event, cancellationToken);

        var (definitionIndex, versionIndex) = await Prefetch(@event.Versions, cancellationToken);

        foreach (var version in @event.Versions)
        {
            await ReconcileVersion(version, definitionIndex, versionIndex, cancellationToken);
        }
    }

    /// <summary>
    /// Read-only prefetch of every definition and version the candidate set could touch, seeding the
    /// two mutable in-loop indexes. Batches read #2 as the union of an <c>Id IN</c> read and an
    /// <c>ActivityTypeKey IN</c> read (identical result set to the per-candidate <c>Id OR key</c>
    /// lookup, deduped by Id) and read #3 as a single <c>DefinitionId IN</c> read. Never throws — all
    /// identity/hash validation stays in the ordered candidate loop so throw ordering is preserved.
    /// </summary>
    private async Task<(DefinitionIndex, VersionIndex)> Prefetch(
        ICollection<IActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken)
    {
        var ids = candidates.Select(v => v.Definition.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        var keys = candidates.Select(v => v.Definition.ActivityTypeKey).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();

        var definitionIndex = new DefinitionIndex();

        if (ids.Length > 0)
            definitionIndex.SeedRange(await definitionStore.ListAsync(new ActivityDefinitionFilter { Ids = ids }, cancellationToken));
        if (keys.Length > 0)
            definitionIndex.SeedRange(await definitionStore.ListAsync(new ActivityDefinitionFilter { ActivityTypeKeys = keys }, cancellationToken));

        var versionIndex = new VersionIndex();
        var knownDefinitionIds = definitionIndex.Ids.ToArray();

        if (knownDefinitionIds.Length > 0)
            versionIndex.SeedRange(await versionStore.ListByDefinitionIdsAsync(knownDefinitionIds, cancellationToken));

        return (definitionIndex, versionIndex);
    }

    private async Task ReconcileVersion(
        IActivityDefinitionVersion incomingVersion,
        DefinitionIndex definitionIndex,
        VersionIndex versionIndex,
        CancellationToken cancellationToken)
    {
        var sourceVersionPublisher = SingleSourceVersionPublisher();
        // The content hash is generated by the version factory; the reconciler only compares it.
        var incomingHash = incomingVersion.Hash ?? string.Empty;

        var definition = definitionIndex.Find(
            incomingVersion.Definition.Id,
            incomingVersion.Definition.ActivityTypeKey
        );

        if (definition is null)
        {
            // Fresh definition + first version (the candidate's own DefinitionId is kept).
            var newDefinition = ActivityDefinition.From(incomingVersion.Definition);
            var newVersion = ActivityDefinitionVersion.From(incomingVersion);
            newVersion.Definition = newDefinition;
            if (sourceVersionPublisher is null)
                await addNewDefinitionCommand.Execute(
                    CreateOperationKey("create", incomingVersion),
                    newDefinition,
                    newVersion,
                    cancellationToken);
            else
                await sourceVersionPublisher.PublishAsync(newDefinition, newVersion, cancellationToken);

            // Mirror the write into the indexes so a later candidate targeting the same definition
            // observes it exactly as a per-candidate re-read would (the same-new-definition-twice case).
            definitionIndex.Add(newDefinition);
            versionIndex.Add(newVersion);
            return;
        }

        // Definition exists. Lookup the specific version by (DefinitionId, Version) — build-metadata-
        // insensitive (FR-013) by matching on the normalised sort key, so 1.0.0 and 1.0.0+build are
        // the same logical version.
        var incomingSortKey = SemVer.ToSortKey(incomingVersion.Version);
        var existingVersion = versionIndex.Find(definition.Id, incomingSortKey);

        if (existingVersion is null)
        {
            // Definition exists but this Version number doesn't. Append, rebinding the version to the
            // existing definition's id while keeping the candidate's content hash unchanged.
            var appended = ActivityDefinitionVersion.From(incomingVersion, definition.Id);
            appended.Definition = definition;
            if (sourceVersionPublisher is null)
                await addVersionCommand.Execute(
                    CreateOperationKey("append", incomingVersion),
                    appended,
                    cancellationToken);
            else
                await sourceVersionPublisher.PublishAsync(definition, appended, cancellationToken);
            versionIndex.Add(appended);
            return;
        }

        // (DefinitionId, Version) is already persisted — Model X duplicate path.
        if (!string.Equals(existingVersion.Hash, incomingHash, StringComparison.Ordinal))
        {
            if (IsBuildMetadataOnlyDuplicate(existingVersion, incomingVersion))
            {
                LogSkipBuildMetadataOnlyDuplicate(definition.Id, definition.ActivityTypeKey, existingVersion.Version, incomingVersion.Version);
                return;
            }

            // Same identity, different content — the source is broken. Throw loudly.
            throw new ActivityVersionHashMismatchException(
                definitionId: definition.Id,
                activityTypeKey: definition.ActivityTypeKey,
                persistedVersion: existingVersion.Version,
                incomingVersion: incomingVersion.Version,
                persistedHash: existingVersion.Hash,
                incomingHash: incomingHash
            );
        }

        // Hash matches → genuine duplicate.
        HandleHashMatchedDuplicate(definition, incomingVersion.Version);
        if (sourceVersionPublisher is not null)
        {
            existingVersion.Definition ??= definition;
            await sourceVersionPublisher.PublishAsync(definition, existingVersion, cancellationToken);
        }
    }

    private IActivitySourceVersionPublisher? SingleSourceVersionPublisher()
    {
        var publishers = (sourceVersionPublishers ?? []).Take(2).ToArray();
        return publishers.Length switch
        {
            0 => null,
            1 => publishers[0],
            _ => throw new InvalidOperationException("Only one source-owned activity publication bridge may be registered.")
        };
    }

    private static DesignOperationKey CreateOperationKey(string operation, IActivityDefinitionVersion version)
    {
        // A reconciliation operation must survive a process restart and must never derive from target
        // document identifiers. Source provenance, activity type and the source revision (content hash)
        // are immutable inputs, so the same source contribution reuses this exact operation key.
        var material = string.Join(
            '\u001f',
            "activity-reconciliation:v1",
            operation,
            version.SourceKind,
            version.SourceId,
            version.Definition.ActivityTypeKey,
            version.Version,
            version.Hash ?? string.Empty);
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return new DesignOperationKey($"activity-reconciliation:{operation}:{Convert.ToHexString(digest)}");
    }

    private void HandleHashMatchedDuplicate(ActivityDefinition definition, string version)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException(
                    $"Activity definition '{definition.ActivityTypeKey}' (id = '{definition.Id}') already has version {version} persisted with matching content hash.");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(definition.Id, definition.ActivityTypeKey, version);
                break;

            default:
                // An out-of-range value (a future member, or an unchecked cast) is a programming error,
                // not a data condition. Fail-fast rather than silently drop the duplicate (issue #417 item 4b).
                throw new InvalidOperationException(
                    $"Unknown {nameof(DuplicateHandling)} value '{options.Value.DuplicateHandling}'.");
        }
    }

    private void LogSkipDuplicate(string definitionId, string activityTypeKey, string version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Reconciliation: skipping hash-matched duplicate. Definition '{key}' (id = '{id}'), version {v}.",
                activityTypeKey, definitionId, version);
    }

    private bool IsBuildMetadataOnlyDuplicate(
        IActivityDefinitionVersion existingVersion,
        IActivityDefinitionVersion incomingVersion)
    {
        if (string.Equals(existingVersion.Version, incomingVersion.Version, StringComparison.Ordinal))
            return false;

        // "Is the ONLY difference the version's build metadata?" To answer that, hash the INCOMING
        // definition + version content with the version's build metadata neutralised by rebasing onto
        // the persisted version string. Hashing the incoming projection (not the persisted definition)
        // is what makes this blind to build metadata alone: any genuine definition-level change
        // (Description/Category/DisplayName) or version-level change flows into the hash and fails the
        // match, so the caller falls through to ActivityVersionHashMismatchException.
        var incomingDefinition = incomingVersion.Definition;
        var incomingRebasedToPersistedVersion = new ActivityDefinitionVersionModel(
            Id: incomingVersion.Id,
            Version: existingVersion.Version,
            DefinitionId: incomingVersion.DefinitionId,
            ProviderKey: incomingVersion.ProviderKey,
            ProviderSchemaVersion: incomingVersion.ProviderSchemaVersion,
            ConsumerKey: incomingVersion.ConsumerKey,
            ConsumerSchemaVersion: incomingVersion.ConsumerSchemaVersion,
            DescriptorPayload: incomingVersion.DescriptorPayload,
            SourceKind: incomingVersion.SourceKind,
            SourceId: incomingVersion.SourceId,
            Definition: incomingDefinition,
            Inputs: incomingVersion.Inputs,
            Outputs: incomingVersion.Outputs,
            DesignFacets: incomingVersion.DesignFacets,
            ExecutionType: incomingVersion.ExecutionType,
            Hash: string.Empty);

        var compatibilityHash = hasher.Hash(incomingDefinition, incomingRebasedToPersistedVersion);
        return string.Equals(existingVersion.Hash, compatibilityHash, StringComparison.Ordinal);
    }

    private void LogSkipBuildMetadataOnlyDuplicate(
        string definitionId,
        string activityTypeKey,
        string persistedVersion,
        string incomingVersion)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Reconciliation: skipping build-metadata-only duplicate. Definition '{key}' (id = '{id}'), persisted version {persistedVersion}, incoming version {incomingVersion}.",
                activityTypeKey,
                definitionId,
                persistedVersion,
                incomingVersion);
    }

    /// <summary>
    /// In-loop mutable projection of the persisted definitions, seeded by the prefetch and updated on
    /// every create. Reproduces <c>IActivityDefinitionStore.FindByIdOrActivityTypeKeyAsync</c> exactly:
    /// match by surrogate Id, else by natural key, and throw the identical identity-collision
    /// <see cref="InvalidOperationException"/> when a matched row's identity contradicts the request.
    /// </summary>
    private sealed class DefinitionIndex
    {
        private readonly Dictionary<string, ActivityDefinition> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ActivityDefinition> _byActivityTypeKey = new(StringComparer.Ordinal);

        public IEnumerable<string> Ids => _byId.Keys;

        public void SeedRange(IEnumerable<ActivityDefinition> definitions)
        {
            foreach (var definition in definitions)
                Add(definition);
        }

        public void Add(ActivityDefinition definition)
        {
            // First-wins mirrors FirstOrDefault over the seed reads; Id and ActivityTypeKey are each
            // unique in practice so collisions are not expected.
            _byId.TryAdd(definition.Id, definition);
            _byActivityTypeKey.TryAdd(definition.ActivityTypeKey, definition);
        }

        public ActivityDefinition? Find(string definitionId, string activityTypeKey)
        {
            // Prefer the surrogate-Id match, then the natural-key match — the two disjuncts of the
            // original Id-OR-key store query.
            var definition = _byId.GetValueOrDefault(definitionId) ?? _byActivityTypeKey.GetValueOrDefault(activityTypeKey);

            if (definition is null)
                return null;

            // Sanity check: if the surrogate Id matched but the natural key disagrees (or vice
            // versa), that's a real identity collision. Throw — the source has produced a row
            // whose logical identity contradicts an existing row's.
            var idMatches = definition.Id == definitionId;
            var naturalKeyMatches = definition.ActivityTypeKey == activityTypeKey;

            if (!idMatches && !naturalKeyMatches)
            {
                throw new InvalidOperationException(
                    $"Activity definition identity mismatch. Trying to reconcile definition (id = '{definitionId}', ActivityTypeKey = '{activityTypeKey}'); found existing definition (id = '{definition.Id}', ActivityTypeKey = '{definition.ActivityTypeKey}').");
            }

            return definition;
        }
    }

    /// <summary>
    /// In-loop mutable projection of the persisted versions keyed by <c>(DefinitionId, SemVerSortKey)</c>,
    /// seeded by the prefetch and updated on every create/append. Reproduces
    /// <c>IActivityDefinitionVersionStore.FindByDefinitionAndSortKeyAsync</c>.
    /// </summary>
    private sealed class VersionIndex
    {
        private readonly Dictionary<(string DefinitionId, string SortKey), ActivityDefinitionVersion> _byDefinitionAndSortKey = new();

        public void SeedRange(IEnumerable<ActivityDefinitionVersion> versions)
        {
            foreach (var version in versions)
                Add(version);
        }

        public void Add(ActivityDefinitionVersion version) =>
            _byDefinitionAndSortKey.TryAdd((version.DefinitionId, version.SemVerSortKey), version);

        public ActivityDefinitionVersion? Find(string definitionId, string semVerSortKey) =>
            _byDefinitionAndSortKey.GetValueOrDefault((definitionId, semVerSortKey));
    }
}
