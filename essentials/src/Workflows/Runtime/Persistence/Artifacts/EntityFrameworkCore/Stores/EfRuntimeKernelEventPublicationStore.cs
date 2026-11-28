using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfRuntimeKernelEventPublicationStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access)
    : IRuntimeKernelEventPublicationStore
{
    public async ValueTask<RuntimeKernelEventPublication?> FindAsync(TriggerScope scope, string slotId, long revision, CancellationToken cancellationToken = default)
    {
        var row = await Scoped(scope).SingleOrDefaultAsync(item => item.SlotHash == Hash(slotId) &&
            item.Revision == revision, cancellationToken);
        return row is null ? null : ReadScoped(row, scope, slotId);
    }

    public async ValueTask<RuntimeKernelEventPublication?> FindCurrentAsync(TriggerScope scope, string slotId,
        CancellationToken cancellationToken = default)
    {
        var row = await Scoped(scope).SingleOrDefaultAsync(item => item.SlotHash == Hash(slotId) && item.Current, cancellationToken);
        return row is null ? null : ReadScoped(row, scope, slotId);
    }

    public ValueTask<IReadOnlyList<RuntimeKernelEventPublication>> ListAsync(TriggerScope scope, bool superseded,
        string? afterSlot, long afterRevision, int limit, CancellationToken cancellationToken = default) =>
        ListBySlotAsync(scope, Eligible(scope, superseded), afterSlot, afterRevision, limit, cancellationToken);

    public ValueTask<IReadOnlyList<RuntimeKernelEventPublication>> ListLegacyScanAsync(TriggerScope scope, bool superseded,
        string? afterSlot, long afterRevision, int limit, CancellationToken cancellationToken = default) =>
        ListBySlotAsync(scope, Eligible(scope, superseded).Where(row => row.InsertionOrdinal == null),
            afterSlot, afterRevision, limit, cancellationToken);

    private static async ValueTask<IReadOnlyList<RuntimeKernelEventPublication>> ListBySlotAsync(TriggerScope scope,
        IQueryable<RuntimeKernelEventPublicationEntity> query, string? afterSlot, long afterRevision, int limit, CancellationToken cancellationToken)
    {
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        if (afterSlot is not null)
        {
            var afterHash = Hash(afterSlot);
            query = query.Where(item => item.SlotHash.CompareTo(afterHash) > 0 ||
                item.SlotHash == afterHash && item.Revision > afterRevision);
        }

        var rows = await query.OrderBy(item => item.SlotHash).ThenBy(item => item.Revision).Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(row => ReadScoped(row, scope)).ToArray();
    }

    public async ValueTask<long> ReadScanTailAsync(TriggerScope scope, CancellationToken cancellationToken = default)
    {
        var tail = await Scoped(scope).Where(row => row.InsertionOrdinal != null).OrderByDescending(row => row.InsertionOrdinal)
            .Select(row => new { row.InsertionOrdinal, row.SchemaVersion, row.Current }).FirstOrDefaultAsync(cancellationToken);
        if (tail is null) return 0;
        EfSchemaVersion.EnsureReadable(RuntimeKernelEventPublicationEfModule.Chain, tail.SchemaVersion);
        var ordinal = tail.InsertionOrdinal.GetValueOrDefault();
        if (ordinal <= 0 || !tail.Current)
            throw new InvalidDataException("The Event publication order does not have a current tail.");
        return ordinal;
    }

    public async ValueTask<IReadOnlyList<RuntimeKernelEventPublicationScanItem>> ListScanAsync(TriggerScope scope, bool superseded,
        long after, long through, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        ArgumentOutOfRangeException.ThrowIfLessThan(through, after);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var rows = await Eligible(scope, superseded).Where(row => row.InsertionOrdinal > after && row.InsertionOrdinal <= through)
            .OrderBy(row => row.InsertionOrdinal).Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(row => new RuntimeKernelEventPublicationScanItem(row.InsertionOrdinal.GetValueOrDefault(), ReadScoped(row, scope))).ToArray();
    }

    public async ValueTask<bool> AcknowledgeAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken = default)
    {
        var query = Exact(publication).Where(row => row.Current);
        var row = await query.SingleOrDefaultAsync(cancellationToken);
        if (row is null) return false;
        ReadScoped(row, publication.Scope, publication.SlotId);
        return row.Applied || await query.Where(item => !item.Applied)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Applied, true), cancellationToken) == 1 ||
            await query.AnyAsync(item => item.Applied, cancellationToken);
    }

    public async ValueTask<bool> ReleaseDrainedAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken = default)
    {
        var query = Exact(publication).Where(row => !row.Current);
        var row = await query.SingleOrDefaultAsync(cancellationToken);
        if (row is null) return false;
        ReadScoped(row, publication.Scope, publication.SlotId);
        return await query.ExecuteDeleteAsync(cancellationToken) == 1;
    }

    internal async ValueTask StageAsync(TriggerScope scope, WorkflowActivationSlot slot, WorkflowExecutableSourceReference? source,
        IReadOnlyList<WorkflowTriggerBinding> bindings, bool inheritTenant, CancellationToken token)
    {
        var scopeHash = Hash(scope.Persistence.Value);
        var slotHash = Hash(slot.SlotId);
        var existing = await context.KernelEventPublications.SingleOrDefaultAsync(row => row.ScopeHash == scopeHash &&
            row.SlotHash == slotHash && row.Current, token);
        var entries = ReadEntries(slot, source, bindings);
        if (existing is null && entries.Length == 0) return;

        // Once reservations disappear, an unapplied withdrawal still belongs to the slot. Carry it into the
        // next transition so that acknowledging that transition also fences out the older physical registration.
        var prior = existing is null ? null : Read(existing, scope.Persistence);
        if (inheritTenant && prior is not null) scope = prior.Scope;
        if (existing is not null)
        {
            ReadScoped(existing, scope, slot.SlotId);
            if (entries.Length == 0 && existing.ArtifactId is null && existing.Applied) return;
        }

        RuntimeKernelEventSource? owner = null;
        if (entries.Length != 0 && source is not null)
        {
            var artifact = bindings[0];
            owner = new(source.ActivationId!, source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion,
                new(source.ArtifactId, source.DefinitionId, source.DefinitionVersionId, source.ArtifactVersion, artifact.ArtifactHash))
            {
                Mode = artifact.Metadata.GetValueOrDefault(RuntimeKernelEventPublication.ModeMetadata) is null ? null : PersistenceMode.Durable
            };
        }
        var publication = new RuntimeKernelEventPublication(scope, slot.SlotId, slot.SlotName, slot.WorkflowDefinitionId, slot.Revision, owner, entries);
        var content = RuntimeArtifactJson.Serialize(publication);
        if (existing is not null)
        {
            if (existing.Revision == slot.Revision && RuntimeArtifactJson.Serialize(prior) == content) return;
            if (existing.Revision >= slot.Revision)
                throw new InvalidDataException("The Event publication generation does not advance its slot's exact revision.");
            if (existing.ArtifactId is null) context.KernelEventPublications.Remove(existing);
            else existing.Current = false;
        }
        // A transition supersedes a current row only when it writes a newer current one. Drain can
        // delete only superseded rows, so the newest ordinal remains a durable tail without a counter row.
        // The activation transaction retries an allocation race on the scoped unique index.
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Assigning an Event publication ordinal requires the activation transaction.");
        var ordinal = checked(await ReadScanTailAsync(scope, token) + 1);
        context.KernelEventPublications.Add(new()
        {
            ScopeHash = scopeHash, SlotHash = slotHash, TenantHash = TenantHash(scope), Revision = slot.Revision,
            InsertionOrdinal = ordinal,
            ArtifactId = owner?.Executable.ArtifactId, ContentJson = content, Current = true,
            SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelEventPublicationEfModule.SchemaFamily,
                RuntimeKernelEventPublicationEfModule.SchemaVersion)
        });
    }

    internal static async ValueTask<IReadOnlyCollection<string>> ReadPinnedExecutableIdsAsync(RuntimeDbContext context, PersistenceScope scope,
        CancellationToken cancellationToken)
    {
        var hash = Hash(scope.Value);
        var rows = context.KernelEventPublications.AsNoTracking().Where(row => row.ScopeHash == hash && row.ArtifactId != null);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(cancellationToken))
            roots.Add(Read(row, scope).Source!.Executable.ArtifactId);
        return roots;
    }

    private IQueryable<RuntimeKernelEventPublicationEntity> Scoped(TriggerScope scope)
    {
        if (scope.Persistence.Value != RequireScope(access))
            throw new InvalidOperationException("Event publication access must use the current exact persistence scope.");
        var scopeHash = Hash(scope.Persistence.Value);
        var tenantHash = TenantHash(scope);
        return context.KernelEventPublications.AsNoTracking().Where(row => row.ScopeHash == scopeHash && row.TenantHash == tenantHash);
    }

    private IQueryable<RuntimeKernelEventPublicationEntity> Exact(RuntimeKernelEventPublication publication) =>
        Scoped(publication.Scope).Where(row => row.SlotHash == Hash(publication.SlotId) &&
            row.Revision == publication.Revision && row.ContentJson == RuntimeArtifactJson.Serialize(publication));

    private IQueryable<RuntimeKernelEventPublicationEntity> Eligible(TriggerScope scope, bool superseded)
    {
        var query = Scoped(scope);
        return superseded ? query.Where(item => !item.Current && item.ArtifactId != null) :
            query.Where(item => item.Current && !item.Applied);
    }

    private static RuntimeKernelEventPublication ReadScoped(RuntimeKernelEventPublicationEntity row, TriggerScope scope, string? slotId = null)
    {
        var value = Read(row, scope.Persistence);
        if (RuntimeKernelScheduleControl.CanonicalScope(value.Scope) != RuntimeKernelScheduleControl.CanonicalScope(scope) ||
            slotId is not null && value.SlotId != slotId)
            throw new InvalidDataException("The stored Event publication does not match its requested slot and tenant.");
        return value;
    }

    private static RuntimeKernelEventPublication Read(RuntimeKernelEventPublicationEntity row, PersistenceScope scope)
    {
        EfSchemaVersion.EnsureReadable(RuntimeKernelEventPublicationEfModule.Chain, row.SchemaVersion);
        var value = RuntimeArtifactJson.Deserialize<RuntimeKernelEventPublication>(row.ContentJson);
        if (value.Scope.Persistence != scope || Hash(scope.Value) != row.ScopeHash || TenantHash(value.Scope) != row.TenantHash ||
            Hash(value.SlotId) != row.SlotHash || value.Revision <= 0 || value.Revision != row.Revision || row.InsertionOrdinal is <= 0 ||
            value.Source?.Executable.ArtifactId != row.ArtifactId || (value.Source is null) != (value.Entries.Count == 0))
            throw new InvalidDataException("The stored Event publication does not match its indexed authority.");
        return value;
    }

    private static RuntimeKernelEventEntry[] ReadEntries(WorkflowActivationSlot slot, WorkflowExecutableSourceReference? source,
        IReadOnlyList<WorkflowTriggerBinding> bindings)
    {
        if (bindings.Count == 0) return [];
        if (source is null || source.ActivationId != slot.ActiveActivationId || source.SlotId != slot.SlotId ||
            source.DefinitionId != slot.WorkflowDefinitionId || source.Scope != WorkflowExecutableReferenceScope.Published ||
            source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(source.ActivationId!))
            throw new InvalidDataException("The Event registration does not have the authoritative publication source.");

        var hash = bindings[0].ArtifactHash;
        var mode = bindings[0].Metadata.GetValueOrDefault(RuntimeKernelEventPublication.ModeMetadata);
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<RuntimeKernelEventEntry>();
        foreach (var binding in bindings.OrderBy(binding => binding.ExecutableNodeId, StringComparer.Ordinal))
        {
            if (binding.Cardinality != TriggerCardinality.ReservationOnly || binding.ActivationId != source.ActivationId ||
                binding.SlotId != slot.SlotId || binding.DefinitionId != source.DefinitionId ||
                binding.ArtifactId != source.ArtifactId || binding.ArtifactVersion != source.ArtifactVersion || binding.ArtifactHash != hash ||
                binding.StimulusHash != RuntimeKernelEventPublication.ReservationKey(slot.SlotId, binding.ExecutableNodeId) ||
                binding.Metadata is null || !binding.Metadata.TryGetValue(RuntimeKernelEventPublication.EventNameMetadata, out var name) ||
                string.IsNullOrWhiteSpace(name) || !nodes.Add(binding.ExecutableNodeId) ||
                mode is not (null or nameof(PersistenceMode.Durable)) ||
                binding.Metadata.GetValueOrDefault(RuntimeKernelEventPublication.ModeMetadata) != mode)
                throw new InvalidDataException("The staged Event binding does not match its authored publication node.");
            entries.Add(new(binding.ExecutableNodeId, name));
        }
        return [.. entries];
    }

    private static string TenantHash(TriggerScope scope) =>
        Hash(RuntimeKernelScheduleControl.CanonicalScope(scope).TenantId ?? "");
}
