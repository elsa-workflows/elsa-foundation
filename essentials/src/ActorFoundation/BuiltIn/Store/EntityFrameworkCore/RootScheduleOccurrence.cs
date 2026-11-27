using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>The immutable authority and delivery identity recorded by the host's accepted occurrence.</summary>
internal sealed record RootScheduleOccurrence(ScheduleRootKey Schedule, string ScheduleReference, long ArmGeneration,
    string TimerDelivery, long Due)
{
    internal static RootScheduleOccurrence FromContract(Contract.AcceptedScheduleOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return new(new(occurrence.DefinitionId, occurrence.ScheduleKey), occurrence.Schedule.Id, occurrence.ArmGeneration,
            occurrence.TimerDeliveryId, occurrence.DueAtUnixMilliseconds);
    }

    internal static StartSpec WorkflowStart(Contract.StartSpec workflow, string workflowType)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.Type != workflowType || workflow.Ephemeral || !Enum.IsDefined(workflow.History))
            throw new ArgumentException("An occurrence must start the configured durable workflow actor type.", nameof(workflow));
        return new(workflow.Type, workflow.TypeData, workflow.Input, History: (History)workflow.History);
    }

    internal void Validate()
    {
        Schedule.Validate();
        if (!FoundationRootCreator.IsGeneratedReference(ScheduleReference) || ArmGeneration <= 0 || Due <= 0 ||
            string.IsNullOrWhiteSpace(TimerDelivery) || !FoundationMetadataLimits.FitsLiveWaitName(TimerDelivery))
            throw new ArgumentException("Invalid schedule occurrence identity.");
    }

    internal string AddressDigest => FoundationRootCreator.OperationDigest(FormattableString.Invariant(
        $"schedule-occurrence:1:{ScheduleReference}:{ArmGeneration}:{TimerDelivery.Length}:{TimerDelivery}"));

    internal string CreatorOperation => FormattableString.Invariant($"schedule-occurrence/{AddressDigest}/{Due}");
}

internal sealed record ScheduledWorkflowBirth(string Reference, Message Creator, string Fingerprint)
{
    internal static ScheduledWorkflowBirth Create(string tenant, RootScheduleOccurrence occurrence, StartSpec specification,
        byte[] issuer, int partitions)
    {
        var address = tenant + "_occurrence_" + occurrence.AddressDigest;
        var actor = InstanceRef.In(InstanceRef.Spread(address, partitions), address);
        var reference = FoundationRootCreator.ReferenceFor(issuer, tenant, new(actor.Id, 1));
        var operation = occurrence.CreatorOperation;
        var creator = FoundationRootCreator.Create(actor, operation, FoundationRootCreator.OperationDigest(operation),
            specification, null, null);
        return new(reference, creator, CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!);
    }

    private sealed record Receipt(bool SourceReady, string? Reference, string? Tenant, string? Actor, int? Partition,
        long? Creation, string? Creator, string? Fingerprint, bool HasLineage, bool Retired);

    /// <summary>Root birth survives retirement, so an admitted occurrence never depends on an expiring ingress receipt.</summary>
    internal async Task<bool> ReadReceiptAsync(KernelDb db, string tenant, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null) =>
        await ReadAsync(db, tenant, false, token, bindSource) is not null;

    // Profile 120 proves the one actor birth. The occurrence lineage profile also proves its descendants are retired.
    internal async Task<bool> IsRetiredAsync(KernelDb db, string tenant, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null) =>
        (await ReadAsync(db, tenant, true, token, bindSource))?.Retired == true;

    private async Task<Receipt?> ReadAsync(KernelDb db, string tenant, bool confirmRetirement, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource)
    {
        var retirement = confirmRetirement ? """
            birth."Reference" IS NOT NULL AND EXISTS (
                SELECT 1 FROM "Instances" instance
                JOIN "Journal" tombstone ON tombstone."Instance" = instance."Id"
                    AND tombstone."Partition" = instance."Partition" AND tombstone."Revision" = -1
                WHERE instance."Id" = birth."Actor" AND instance."Partition" = birth."Partition"
                    AND instance."FoundationTenant" = birth."Tenant" AND instance."FoundationReference" = birth."Reference"
                    AND instance."Retired" AND tombstone."Entries" = '["delete"]')
            """ + (db.UsesRootScheduleLineage ? "\n" + RootScheduleLineage.RootMembershipProof : "") : "false";
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", birth."Reference", birth."Tenant", birth."Actor", birth."Partition",
                birth."Creation", birth."Creator", birth."Fingerprint", /*lineage-receipt*/ AS "HasLineage",
                /*retirement-proof*/ AS "Retired"
            FROM source LEFT JOIN "FoundationRootBirths" birth ON source."SourceReady" AND birth."Reference" = {Reference}
            """;
        query = FormattableStringFactory.Create(query.Format
            .Replace("/*lineage-receipt*/", db.UsesRootScheduleLineage ? """
                EXISTS (SELECT 1 FROM /*lineage-table*/ lineage WHERE lineage."Reference" = birth."Reference"
                    AND lineage."Tenant" = birth."Tenant" AND lineage."RootReference" = birth."Reference")
                """ : "true", StringComparison.Ordinal)
            .Replace("/*retirement-proof*/", retirement, StringComparison.Ordinal)
            .Replace("/*lineage-table*/", RootScheduleLineage.PhysicalTable(db), StringComparison.Ordinal), query.GetArguments());
        var row = await db.Database.SqlQuery<Receipt>(RootScheduleInventory.Bind(query, bindSource)).SingleAsync(token);
        if (!row.SourceReady) throw new StoreFormatException("The pinned schedule store changed during occurrence lookup.");
        if (row.Reference is null) return null;
        if (row.Tenant != tenant || row.Actor != Creator.Target.Id || row.Partition != Creator.Target.Partition || row.Creation != 1 ||
            row.Creator != Creator.InboxKey.Id || row.Fingerprint != Fingerprint)
            throw new StoreFormatException("The scheduled workflow birth differs from its accepted occurrence.");
        if (!row.HasLineage) throw new StoreFormatException("The scheduled workflow birth has no exact lineage receipt.");
        return row;
    }
}
