using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeKernelScheduleOccurrenceStore
{
    private sealed record RetiredOccurrence(string OccurrenceId, string ActorReference, string IntentFingerprint);

    public async ValueTask<IReadOnlyList<RuntimeKernelScheduleMaterializedOccurrence>> ListMaterializedAsync(TriggerScope scope,
        RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default)
    {
        var page = await ReadPageAsync(scope, after, limit, materialized: true, cancellationToken);
        return page.Select(row => new RuntimeKernelScheduleMaterializedOccurrence(ReadScoped(row, scope), row.ActorReference!,
            new(row.AcceptedAtUtcTicks, row.Id))).ToArray();
    }

    public async ValueTask<bool> TryRetireAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference,
        Func<RuntimeKernelScheduleOccurrence, string, CancellationToken, ValueTask<bool>> confirmRetired,
        CancellationToken cancellationToken = default)
    {
        Validate(occurrence);
        ArgumentNullException.ThrowIfNull(confirmRetired);
        if (!IsReference(actorReference)) throw new ArgumentException("Invalid scheduled actor reference.", nameof(actorReference));
        var scope = Bind(occurrence.Scope);
        var id = CompositeId(scope, occurrence.Id);
        var current = await RequireMaterializedAsync();
        if (current.RetiredAtUtcTicks is not null) return true;
        var schemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelScheduleOccurrenceEfModule.SchemaFamily,
            RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion);
        if (schemaVersion != RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion)
            throw new NotSupportedException("The active Runtime schema does not support scheduled retirement receipts.");
        if (!await confirmRetired(occurrence, actorReference, cancellationToken)) return false;

        var content = RuntimeArtifactJson.Serialize(new RetiredOccurrence(occurrence.Id, actorReference, IntentFingerprint(occurrence)));
        var retiredAt = clock.GetUtcNow().UtcTicks;
        var count = await context.KernelScheduleOccurrences.Where(row => row.Id == id && row.Revision == current.Revision &&
                row.ActorReference == actorReference && row.RetiredAtUtcTicks == null && row.ContentJson == current.ContentJson)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.RetiredAtUtcTicks, retiredAt)
                .SetProperty(row => row.ContentJson, content).SetProperty(row => row.ArtifactId, string.Empty)
                .SetProperty(row => row.SchemaVersion, schemaVersion).SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
        return count == 1 || (await RequireMaterializedAsync()).RetiredAtUtcTicks is not null;

        async Task<RuntimeKernelScheduleOccurrenceEntity> RequireMaterializedAsync()
        {
            var row = await RequireOccurrenceAsync(scope, id, occurrence, cancellationToken);
            if (row.ActorReference != actorReference)
                throw new InvalidDataException("Retirement requires the exact acknowledged scheduled workflow birth.");
            return row;
        }
    }

    private static bool MatchesOccurrence(RuntimeKernelScheduleOccurrenceEntity row, string scope, RuntimeKernelScheduleOccurrence occurrence)
    {
        if (row.RetiredAtUtcTicks is null) return Read(row, scope) == occurrence;
        if (row.Revision != 3 || row.RetiredAtUtcTicks <= 0 || !IsReference(row.ActorReference) || row.ArtifactId != string.Empty ||
            row.SchemaVersion != RuntimeKernelScheduleOccurrenceEfModule.SchemaVersion)
            throw new InvalidDataException("The schedule retirement receipt has an invalid physical state.");
        var retired = RuntimeArtifactJson.Deserialize<RetiredOccurrence>(row.ContentJson);
        var tenant = JsonSerializer.Serialize(occurrence.Scope.TenantId);
        if (retired.ActorReference != row.ActorReference || !IsReference(retired.IntentFingerprint) ||
            row.Id != CompositeId(scope, retired.OccurrenceId) || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
            row.TenantKey != Encode(tenant) || row.TenantKeyHash != Hash(tenant))
            throw new InvalidDataException("The schedule retirement receipt differs from its physical identity.");
        return retired.OccurrenceId == occurrence.Id && retired.IntentFingerprint == IntentFingerprint(occurrence) &&
            row.AcceptedAtUtcTicks >= occurrence.Due.UtcTicks && row.AcceptedAtUtcTicks < occurrence.AcceptBefore.UtcTicks;
    }

    private static string IntentFingerprint(RuntimeKernelScheduleOccurrence occurrence) =>
        Hash(RuntimeArtifactJson.Serialize(occurrence with { Due = occurrence.Due.ToUniversalTime(), AcceptBefore = occurrence.AcceptBefore.ToUniversalTime() }));
}
