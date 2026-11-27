using System.Data;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;
using AttemptOutcome = Elsa.Workflows.Runtime.Core.StandaloneTriggers.RuntimeKernelScheduleAttemptOutcome;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeKernelScheduleOccurrenceStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor access,
    IWorkflowExecutableStore executables,
    IWorkflowExecutableSourceReferenceStore sourceReferences,
    IWorkflowTriggerBindingStore triggerBindings,
    IWorkflowExecutableRootWriteLeaseManager leases,
    TimeProvider clock) : IRuntimeKernelScheduleOccurrenceStore, IRuntimeKernelScheduleOccurrenceDeliveryStore,
        IRuntimeKernelScheduleOccurrenceRetirementStore, IRuntimeKernelScheduleRetentionRoots, IRuntimeMaterialRetentionRootSource
{
    private sealed record RenewedAcceptance(RuntimeKernelScheduleOccurrence Occurrence, long AttemptDeadlineUtcTicks);

    public async ValueTask<bool> TryAcceptAsync(RuntimeKernelScheduleOccurrence occurrence,
        Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return await TryAcceptAttemptAsync(occurrence, occurrence.AcceptBefore, readScheduleClock, cancellationToken) == AttemptOutcome.Accepted;
    }

    public async ValueTask<AttemptOutcome> TryAcceptAttemptAsync(RuntimeKernelScheduleOccurrence occurrence, DateTimeOffset attemptDeadline,
        Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock, CancellationToken cancellationToken = default)
    {
        Validate(occurrence);
        if (attemptDeadline < occurrence.AcceptBefore)
            throw new ArgumentOutOfRangeException(nameof(attemptDeadline), "A renewed attempt cannot shorten the original offer.");
        ArgumentNullException.ThrowIfNull(readScheduleClock);
        var scope = Bind(occurrence.Scope);
        if (!context.Database.IsRelational() ||
            executables.GetMaterialStore() is not EfWorkflowExecutableStore material || !ReferenceEquals(material.Context, context) ||
            sourceReferences is not EfWorkflowExecutableSourceReferenceStore sources || !ReferenceEquals(sources.Context, context) ||
            triggerBindings is not EfWorkflowTriggerBindingStore bindings || !ReferenceEquals(bindings.Context, context) ||
            context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new NotSupportedException("Schedule acceptance requires one clean, scoped Runtime database and its physical publication stores.");

        var id = CompositeId(scope, occurrence.Id);
        if (await ReplayAsync(cancellationToken)) return AttemptOutcome.Accepted;
        if (!IsDue(await readScheduleClock(cancellationToken))) return AttemptOutcome.Retry;

        var outcome = AttemptOutcome.Retry;
        try
        {
            await leases.ExecuteFencedAsync([occurrence.Executable], $"kernel-schedule:{occurrence.Id}:{Guid.NewGuid():N}", async (grants, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                try
                {
                    if (await ReplayAsync(token))
                    {
                        outcome = AttemptOutcome.Accepted;
                        return;
                    }
                    var deadlines = await material.FenceRootWriteLeasesAsync(grants, clock, token);
                    var slot = await new EfWorkflowActivationAuthority(context, access).FindAsync(occurrence.Executable.DefinitionId, occurrence.SlotName, token);
                    if (slot is null || slot.ActiveActivationId != occurrence.ActivationId || slot.Revision != occurrence.PublicationRevision)
                    {
                        outcome = AttemptOutcome.Refused;
                        return;
                    }
                    var published = await FencePublicationAsync(scope, occurrence, slot, sources, bindings, token);
                    if (published is null) return;
                    var acceptedAt = await readScheduleClock(token);
                    if (!IsDue(acceptedAt)) return;

                    var tenant = JsonSerializer.Serialize(occurrence.Scope.TenantId);
                    var renewed = attemptDeadline > occurrence.AcceptBefore;
                    context.KernelScheduleOccurrences.Add(new()
                    {
                        Id = id,
                        ScopeKey = Encode(scope),
                        ScopeKeyHash = Hash(scope),
                        TenantKey = Encode(tenant),
                        TenantKeyHash = Hash(tenant),
                        ArtifactId = Encode(occurrence.Executable.ArtifactId),
                        ContentJson = renewed ? RuntimeArtifactJson.Serialize(new RenewedAcceptance(occurrence, attemptDeadline.UtcTicks))
                            : RuntimeArtifactJson.Serialize(occurrence),
                        SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelScheduleOccurrenceEfModule.SchemaFamily,
                            renewed ? RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion : RuntimeKernelScheduleOccurrenceEfModule.RetirementSchemaVersion),
                        Revision = 1,
                        AcceptedAtUtcTicks = acceptedAt.UtcTicks
                    });
                    await context.SaveChangesAsync(token);
                    if (!IsDue(await readScheduleClock(token)) || !published.IsLive(clock.GetUtcNow())) return;
                    foreach (var deadline in deadlines)
                        if (clock.GetUtcNow() >= deadline.ExpiresAt)
                            throw new WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
                    await transaction.CommitAsync(token);
                    outcome = AttemptOutcome.Accepted;
                }
                finally { context.ChangeTracker.Clear(); }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Reconcile a concurrent identical acceptance or a lost acknowledgement after its transaction was disposed.
            if (!await ReplayAsync(cancellationToken)) throw;
            return AttemptOutcome.Accepted;
        }
        return outcome;

        async Task<bool> ReplayAsync(CancellationToken token)
        {
            var row = await FindAsync(id, token);
            if (row is null) return false;
            if (!MatchesOccurrence(row, scope, occurrence))
                throw new InvalidDataException("A schedule occurrence cannot change its accepted workflow or delivery identity.");
            return true;
        }

        bool IsDue(DateTimeOffset now) => occurrence.Due <= now && attemptDeadline > now;
    }

    public ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadPendingTailAsync(TriggerScope scope, CancellationToken cancellationToken = default) =>
        ReadTailAsync(scope, materialized: false, cancellationToken);

    public ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default) =>
        ReadPendingPageAsync(scope, after, null, limit, cancellationToken);

    public ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor through, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(through);
        return ReadPendingPageAsync(scope, after, through, limit, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ReadPendingPageAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor? through, int limit, CancellationToken token)
    {
        var page = await ReadPageAsync(scope, after, through, limit, materialized: false, token);
        return page.Select(row => new RuntimeKernelScheduleOccurrenceDelivery(ReadScoped(row, scope), new(row.AcceptedAtUtcTicks, row.Id))).ToArray();
    }

    private async ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadTailAsync(TriggerScope scope, bool materialized, CancellationToken token) =>
        await ScopedOccurrences(scope, materialized).OrderByDescending(row => row.AcceptedAtUtcTicks).ThenByDescending(row => row.Id)
            .Select(row => new RuntimeKernelScheduleOccurrenceCursor(row.AcceptedAtUtcTicks, row.Id)).FirstOrDefaultAsync(token);

    private async Task<RuntimeKernelScheduleOccurrenceEntity[]> ReadPageAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor? through, int limit, bool materialized,
        CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        var query = ScopedOccurrences(scope, materialized);
        if (after is not null)
        {
            RequireCursor(after, nameof(after));
            query = query.Where(row => row.AcceptedAtUtcTicks > after.AcceptedAtUtcTicks ||
                row.AcceptedAtUtcTicks == after.AcceptedAtUtcTicks && string.Compare(row.Id, after.Id) > 0);
        }
        if (through is not null)
        {
            RequireCursor(through, nameof(through));
            query = query.Where(row => row.AcceptedAtUtcTicks < through.AcceptedAtUtcTicks ||
                row.AcceptedAtUtcTicks == through.AcceptedAtUtcTicks && string.Compare(row.Id, through.Id) <= 0);
        }
        return await query.OrderBy(row => row.AcceptedAtUtcTicks).ThenBy(row => row.Id).Take(limit).ToArrayAsync(token);
    }

    private IQueryable<RuntimeKernelScheduleOccurrenceEntity> ScopedOccurrences(TriggerScope scope, bool materialized)
    {
        var scopeKey = Bind(scope);
        var scopeHash = Hash(scopeKey);
        var tenantHash = Hash(JsonSerializer.Serialize(scope.TenantId));
        var query = context.KernelScheduleOccurrences.AsNoTracking().Where(row =>
            row.ScopeKeyHash == scopeHash && row.TenantKeyHash == tenantHash && row.RetiredAtUtcTicks == null);
        return materialized ? query.Where(row => row.ActorReference != null) : query.Where(row => row.ActorReference == null);
    }

    private static void RequireCursor(RuntimeKernelScheduleOccurrenceCursor cursor, string parameter)
    {
        if (cursor.AcceptedAtUtcTicks <= 0 || !IsReference(cursor.Id))
            throw new ArgumentException("Invalid schedule delivery cursor.", parameter);
    }

    private static RuntimeKernelScheduleOccurrence ReadScoped(RuntimeKernelScheduleOccurrenceEntity row, TriggerScope scope)
    {
        var occurrence = Read(row, scope.Persistence.Value);
        if (occurrence.Scope != scope)
            throw new InvalidDataException("A schedule occurrence differs from the requested tenant.");
        return occurrence;
    }

    public async ValueTask MarkMaterializedAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference, CancellationToken cancellationToken = default)
    {
        Validate(occurrence);
        if (!IsReference(actorReference)) throw new ArgumentException("Invalid scheduled actor reference.", nameof(actorReference));
        var scope = Bind(occurrence.Scope);
        var id = CompositeId(scope, occurrence.Id);
        var current = await RequireOccurrenceAsync(scope, id, occurrence, cancellationToken);
        if (current.ActorReference is null)
        {
            var updated = await context.KernelScheduleOccurrences.Where(row => row.Id == id && row.Revision == current.Revision &&
                row.ActorReference == null && row.RetiredAtUtcTicks == null)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.ActorReference, actorReference)
                    .SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (updated == 1) return;
            current = await RequireOccurrenceAsync(scope, id, occurrence, cancellationToken);
        }
        if (!StringComparer.Ordinal.Equals(current.ActorReference, actorReference))
            throw new InvalidDataException("The occurrence could not acknowledge the same scheduled workflow birth.");

    }

    private async Task<RuntimeKernelScheduleOccurrenceEntity> RequireOccurrenceAsync(string scope, string id,
        RuntimeKernelScheduleOccurrence occurrence, CancellationToken token)
    {
        var row = await FindAsync(id, token);
        if (row is null || !MatchesOccurrence(row, scope, occurrence))
            throw new InvalidDataException("The operation requires the exact accepted schedule occurrence.");
        return row;
    }

    private Task<RuntimeKernelScheduleOccurrenceEntity?> FindAsync(string id, CancellationToken token) =>
        context.KernelScheduleOccurrences.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, token);

    private string Bind(TriggerScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var key = RequireScope(access);
        access.Current.EnsureTenantScope(scope.TenantId);
        if (!StringComparer.Ordinal.Equals(key, scope.Persistence.Value))
            throw new InvalidOperationException("The occurrence scope differs from the current Runtime persistence scope.");
        return key;
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FencePublicationAsync(string scope, RuntimeKernelScheduleOccurrence occurrence,
        WorkflowActivationSlot slot, EfWorkflowExecutableSourceReferenceStore sources, EfWorkflowTriggerBindingStore bindings, CancellationToken token)
    {
        var identity = occurrence.Executable;
        if (!await EfAdmissionTargetFence.LockSlotAsync(context, scope, identity.DefinitionId, occurrence.SlotName, slot, token))
            return null;

        var publication = await sources.ReadPhysicalMaintenanceSourceAsync(scope, WorkflowActivationReferenceIdentity.Create(occurrence.ActivationId), token);
        if (publication?.Value is not { Scope: WorkflowExecutableReferenceScope.Published } source ||
            source.TenantId != occurrence.Scope.TenantId || source.ActivationId != occurrence.ActivationId || source.SlotId != slot.SlotId ||
            (source.PublicationScope is not null && source.PublicationScope != occurrence.Scope.Persistence) ||
            source.DefinitionId != identity.DefinitionId || source.DefinitionVersionId != identity.DefinitionVersionId ||
            source.ArtifactId != identity.ArtifactId || source.ArtifactVersion != identity.ArtifactVersion || !source.IsLive(clock.GetUtcNow()) ||
            !await EfAdmissionTargetFence.LockSourceAsync(sources, scope, source, token))
            return null;

        const string type = RuntimeKernelScheduleOccurrence.ReservationType;
        var key = RuntimeKernelScheduleOccurrence.ReservationKey(occurrence.ScheduleKey);
        var bindingId = WorkflowTriggerBinding.BuildId(occurrence.ActivationId, identity.ArtifactId, occurrence.TriggerNode, key);
        var binding = await bindings.FenceBindingAsync(bindingId, token);
        if (binding is not { IsActive: true, Cardinality: TriggerCardinality.ReservationOnly } ||
            binding.ActivationId != occurrence.ActivationId || binding.SlotId != slot.SlotId ||
            binding.DefinitionId != identity.DefinitionId || binding.ArtifactId != identity.ArtifactId ||
            binding.ArtifactHash != identity.ArtifactHash || binding.ArtifactVersion != identity.ArtifactVersion ||
            binding.ExecutableNodeId != occurrence.TriggerNode || binding.StimulusType != type || binding.StimulusHash != key)
            return null;

        var reservations = new EfIngressReservationStore(context, access);
        var reservation = await reservations.FindAsync(occurrence.Scope, type, key, token);
        if (reservation is null || reservation.OwnerKind != RuntimeIngressReservation.PublicationSlotOwner ||
            reservation.OwnerId != slot.SlotId || reservation.OwnerRevision != slot.Revision || reservation.ConfigurationRevision != slot.Revision)
            return null;
        try { await reservations.FenceAsync(reservation, token); }
        catch (TriggerOperationException error) when (error.Code == "conflict") { return null; }
        return source;
    }

    bool IRuntimeMaterialRetentionRootSource.IsCoveredBy(IWorkflowExecutableStore material) =>
        material is EfWorkflowExecutableStore physical && ReferenceEquals(physical.Context, context);

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
        ReadPinnedExecutableIdsAsync(context, RequireScope(access), cancellationToken);

    internal static async ValueTask<IReadOnlyCollection<string>> ReadPinnedExecutableIdsAsync(RuntimeDbContext context,
        string scope, CancellationToken cancellationToken)
    {
        var scopeKey = Encode(scope);
        var scopeHash = Hash(scope);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var query = context.KernelScheduleOccurrences.AsNoTracking().Where(row =>
            (row.ScopeKeyHash == scopeHash || row.ScopeKey == scopeKey) && row.RetiredAtUtcTicks == null);
        await foreach (var row in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
            roots.Add(Read(row, scope).Executable.ArtifactId);
        return roots;
    }

    private static RuntimeKernelScheduleOccurrence Read(RuntimeKernelScheduleOccurrenceEntity row, string scope) => Read(row, scope, out _);

    private static RuntimeKernelScheduleOccurrence Read(RuntimeKernelScheduleOccurrenceEntity row, string scope, out long attemptDeadlineUtcTicks)
    {
        var validState = row switch
        {
            { Revision: 1, ActorReference: null, RetiredAtUtcTicks: null } => true,
            { Revision: 2, RetiredAtUtcTicks: null } => IsReference(row.ActorReference),
            _ => false
        };
        if (!validState || row.AcceptedAtUtcTicks <= 0 || EfSchemaVersion.NotReadable(RuntimeKernelScheduleOccurrenceEfModule.Chain, row.SchemaVersion))
            throw new InvalidDataException("The accepted schedule occurrence has an invalid revision or schema.");
        using var document = JsonDocument.Parse(row.ContentJson);
        var renewed = document.RootElement.TryGetProperty("occurrence", out _)
            ? RuntimeArtifactJson.Deserialize<RenewedAcceptance>(row.ContentJson)
            : null;
        if (renewed is not null && row.SchemaVersion != RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion)
            throw new InvalidDataException("The stored schedule receipt does not support renewed acceptance.");
        var occurrence = renewed is null
            ? RuntimeArtifactJson.Deserialize<RuntimeKernelScheduleOccurrence>(row.ContentJson)
            : renewed.Occurrence;
        Validate(occurrence);
        attemptDeadlineUtcTicks = renewed?.AttemptDeadlineUtcTicks ?? occurrence.AcceptBefore.UtcTicks;
        var tenant = JsonSerializer.Serialize(occurrence.Scope.TenantId);
        if (occurrence.Scope.Persistence.Value != scope || row.Id != CompositeId(scope, occurrence.Id) ||
            row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.TenantKey != Encode(tenant) ||
            row.TenantKeyHash != Hash(tenant) || row.ArtifactId != Encode(occurrence.Executable.ArtifactId) ||
            !MatchesAcceptanceTime(row.AcceptedAtUtcTicks, occurrence, renewed?.AttemptDeadlineUtcTicks))
            throw new InvalidDataException("The accepted schedule occurrence differs from its physical identity.");
        return occurrence;
    }

    private static bool MatchesAcceptanceTime(long acceptedAt, RuntimeKernelScheduleOccurrence occurrence, long? renewedBefore)
    {
        var originalDeadline = occurrence.AcceptBefore.UtcTicks;
        return (renewedBefore is null || renewedBefore > originalDeadline && renewedBefore <= DateTimeOffset.MaxValue.UtcTicks) &&
            acceptedAt >= occurrence.Due.UtcTicks && acceptedAt < (renewedBefore ?? originalDeadline);
    }

    private static void Validate(RuntimeKernelScheduleOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(occurrence.Scope);
        ArgumentNullException.ThrowIfNull(occurrence.Executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.ScheduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.SlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.ActivationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.TriggerNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.TimerDeliveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.PinnedStart);
        if (!IsReference(occurrence.ScheduleReference) ||
            occurrence.PublicationRevision <= 0 || occurrence.ArmGeneration <= 0 ||
            occurrence.Due <= DateTimeOffset.UnixEpoch || occurrence.AcceptBefore <= occurrence.Due ||
            Encoding.UTF8.GetByteCount(occurrence.PinnedStart) > 128 * 1024)
            throw new ArgumentException("Invalid scheduled occurrence identity, interval, or start payload.", nameof(occurrence));
        using var payload = JsonDocument.Parse(occurrence.PinnedStart);
        if (payload.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The scheduled start payload must be a JSON object.", nameof(occurrence));
    }

    private static bool IsReference(string? reference) =>
        reference is { Length: 64 } && reference.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") < 0;
}
