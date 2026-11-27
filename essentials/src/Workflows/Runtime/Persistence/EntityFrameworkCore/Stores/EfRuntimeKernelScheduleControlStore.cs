using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Core.StandaloneTriggers.RuntimeKernelScheduleControl;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfRuntimeKernelScheduleControlStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IRuntimeKernelScheduleControlReader
{
    public async ValueTask<RuntimeKernelScheduleControl?> FindAsync(TriggerScope scope, string scheduleKey, CancellationToken cancellationToken = default)
    {
        Bind(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleKey);
        var id = Identity(scope, scheduleKey);
        var row = await context.KernelScheduleControls.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return null;
        var control = ReadScoped(row, scope);
        if (control.ScheduleKey != scheduleKey) throw InvalidControl();
        return control;
    }

    public async ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, int limit, CancellationToken cancellationToken = default)
    {
        Bind(scope);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var scopeHash = Hash(scope.Persistence.Value);
        var tenantHash = TenantHash(scope);
        var query = context.KernelScheduleControls.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.TenantKeyHash == tenantHash);
        if (after is not null)
        {
            if (after.Length != 64 || after.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0)
                throw new ArgumentException("Invalid schedule control cursor.", nameof(after));
            query = query.Where(row => string.Compare(row.Id, after) > 0);
        }
        var page = await query.OrderBy(row => row.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new(page.Take(limit).Select(row => ReadScoped(row, scope)).ToArray(), page.Length > limit ? page[limit - 1].Id : null);
    }

    // Only the activation switch stages these rows, after staging the exact reservation in the same Serializable transaction.
    internal async ValueTask StageAsync(PersistenceScope persistence, string slotId, IReadOnlyList<RuntimeKernelScheduleControl> desired,
        CancellationToken cancellationToken, bool adoptSchedules = false)
    {
        if (context.Database.CurrentTransaction is null || access.Current.RequireScope() != persistence)
            throw new InvalidOperationException("Schedule controls require the owning publication transaction.");
        var pending = desired.ToDictionary(item => Identity(item.Scope, item.ScheduleKey), StringComparer.Ordinal);
        var ids = pending.Keys.ToArray();
        var scopeHash = Hash(persistence.Value);
        var slotHash = Hash(slotId);
        var rows = await context.KernelScheduleControls.Where(row => row.ScopeKeyHash == scopeHash &&
            (row.OwnerSlotHash == slotHash || ids.Contains(row.Id))).OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var row in rows)
        {
            var current = Read(row, persistence);
            if (current.Owner is { } active && active.SlotId != slotId)
                throw new TriggerOperationException("route-conflict", "Another published workflow owns this schedule.");
            if (pending.Remove(row.Id, out var replacement))
            {
                if (CanonicalScope(current.Scope) != CanonicalScope(replacement.Scope) || current.ScheduleKey != replacement.ScheduleKey ||
                    current.DefinitionId != replacement.DefinitionId || current.TriggerNode != replacement.TriggerNode)
                    throw InvalidControl();
                if (current.Owner != replacement.Owner || current.Scope != replacement.Scope)
                {
                    if (adoptSchedules && current.Owner is not null) throw InvalidControl();
                    if (current.Owner is { } prior && prior.PublicationRevision >= replacement.Owner!.PublicationRevision)
                        throw InvalidControl();
                    Write(row, replacement with { Revision = checked(current.Revision + 1) });
                }
            }
            else if (!adoptSchedules && current.Owner is not null)
                Write(row, current with { Owner = null, Revision = checked(current.Revision + 1) });
            else throw InvalidControl();
        }
        foreach (var (id, control) in pending.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var row = new RuntimeKernelScheduleControlEntity { Id = id, ScopeKeyHash = scopeHash, TenantKeyHash = TenantHash(control.Scope) };
            Write(row, control with { Revision = 1 });
            context.KernelScheduleControls.Add(row);
        }
    }

    internal static RuntimeKernelScheduleControl[] Describe(TriggerScope scope, WorkflowActivationSlot slot,
        WorkflowExecutableSourceReference source, IReadOnlyList<WorkflowTriggerBinding> bindings)
    {
        var controls = new List<RuntimeKernelScheduleControl>();
        foreach (var binding in bindings.Where(item => item.StimulusType == RuntimeKernelScheduleOccurrence.ReservationType))
        {
            var node = binding.ExecutableNodeId;
            if (slot.Revision <= 0 || slot.SlotId != WorkflowActivationSlotIdentity.Create(slot.WorkflowDefinitionId, slot.SlotName) ||
                source.Scope != WorkflowExecutableReferenceScope.Published ||
                source.PublicationScope is { } publicationScope && publicationScope != scope.Persistence ||
                scope.TenantId is { } tenant && tenant != scope.Persistence.Value ||
                source.DefinitionId != slot.WorkflowDefinitionId ||
                source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(source.ActivationId!) ||
                binding.ActivationId != source.ActivationId || binding.SlotId != slot.SlotId ||
                binding.DefinitionId != source.DefinitionId || binding.ArtifactId != source.ArtifactId || binding.ArtifactVersion != source.ArtifactVersion ||
                string.IsNullOrWhiteSpace(node) || string.IsNullOrWhiteSpace(binding.ArtifactHash) ||
                binding.Cardinality != TriggerCardinality.ReservationOnly ||
                !binding.Metadata.TryGetValue(RuntimeKernelScheduleControl.ScheduleKeyMetadata, out var key) ||
                key != LogicalKey(source.DefinitionId, node) || binding.StimulusHash != RuntimeKernelScheduleOccurrence.ReservationKey(key) ||
                binding.TriggerBindingId != WorkflowTriggerBinding.BuildId(source.ActivationId!, source.ArtifactId, node, binding.StimulusHash))
                throw new InvalidDataException("The schedule binding does not match its exact logical key and publication source.");

            var executable = new WorkflowExecutableIdentity(source.ArtifactId, source.DefinitionId, source.DefinitionVersionId,
                source.ArtifactVersion, binding.ArtifactHash);
            var owner = new RuntimeKernelScheduleControlOwner(slot.SlotId, slot.SlotName, source.ActivationId!, slot.Revision,
                source.SourceReferenceId, executable);
            controls.Add(new(scope, key, source.DefinitionId, node, 0, owner));
        }
        if (controls.Select(control => control.ScheduleKey).Distinct(StringComparer.Ordinal).Count() != controls.Count)
            throw new InvalidDataException("One publication cannot describe the same logical schedule twice.");
        return [.. controls];
    }

    private void Bind(TriggerScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!context.Database.IsRelational() || RequireScope(access) != scope.Persistence.Value)
            throw new InvalidOperationException("The schedule control scope differs from the selected Runtime persistence scope.");
        access.Current.EnsureTenantScope(scope.TenantId);
    }

    private void Write(RuntimeKernelScheduleControlEntity row, RuntimeKernelScheduleControl control)
    {
        row.ContentJson = RuntimeArtifactJson.Serialize(control);
        row.OwnerSlotHash = control.Owner is { } owner ? Hash(owner.SlotId) : null;
        row.Revision = control.Revision;
        row.SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelScheduleControlEfModule.SchemaFamily,
            RuntimeKernelScheduleControlEfModule.SchemaVersion, row.SchemaVersion);
    }

    private static RuntimeKernelScheduleControl ReadScoped(RuntimeKernelScheduleControlEntity row, TriggerScope scope)
    {
        var control = Read(row, scope.Persistence);
        return CanonicalScope(control.Scope) == CanonicalScope(scope) ? control : throw InvalidControl();
    }

    private static RuntimeKernelScheduleControl Read(RuntimeKernelScheduleControlEntity row, PersistenceScope physicalScope)
    {
        if (row.Revision <= 0 || EfSchemaVersion.NotReadable(RuntimeKernelScheduleControlEfModule.Chain, row.SchemaVersion))
            throw InvalidControl();
        var control = RuntimeArtifactJson.Deserialize<RuntimeKernelScheduleControl>(row.ContentJson);
        if (control.Scope is null || control.Scope.Persistence != physicalScope || control.Revision != row.Revision ||
            string.IsNullOrWhiteSpace(control.DefinitionId) || string.IsNullOrWhiteSpace(control.TriggerNode) ||
            control.ScheduleKey != LogicalKey(control.DefinitionId, control.TriggerNode) || row.Id != Identity(control.Scope, control.ScheduleKey) ||
            row.ScopeKeyHash != Hash(physicalScope.Value) || row.TenantKeyHash != TenantHash(control.Scope) ||
            (control.Owner is { } owner ? !MatchesOwner(row, control.DefinitionId, owner) : row.OwnerSlotHash is not null))
            throw InvalidControl();
        return control;
    }

    private static bool MatchesOwner(RuntimeKernelScheduleControlEntity row, string definition, RuntimeKernelScheduleControlOwner owner) =>
        owner.PublicationRevision > 0 && owner.Executable is { } executable && executable.DefinitionId == definition &&
        !string.IsNullOrWhiteSpace(owner.SlotName) && !string.IsNullOrWhiteSpace(owner.ActivationId) &&
        owner.SlotId == WorkflowActivationSlotIdentity.Create(definition, owner.SlotName) &&
        owner.SourceReferenceId == WorkflowActivationReferenceIdentity.Create(owner.ActivationId) && row.OwnerSlotHash == Hash(owner.SlotId);

    private static string LogicalKey(string definition, string node) => $"schedule:{definition}:{node}";

    private static string TenantHash(TriggerScope scope) => Hash(JsonSerializer.Serialize(CanonicalScope(scope).TenantId));

    private static string Identity(TriggerScope scope, string key) =>
        CompositeId(scope.Persistence.Value, "kernel-schedule-control-v1", JsonSerializer.Serialize(CanonicalScope(scope).TenantId), key);

    private static InvalidDataException InvalidControl() => new("A schedule control differs from its physical identity or authority.");
}
