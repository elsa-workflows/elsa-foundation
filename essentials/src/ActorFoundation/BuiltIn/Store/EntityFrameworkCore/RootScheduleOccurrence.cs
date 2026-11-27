using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>The immutable authority and delivery identity recorded by the host's accepted occurrence.</summary>
internal sealed record RootScheduleOccurrence(ScheduleRootKey Schedule, string ScheduleReference, long ArmGeneration,
    string TimerDelivery, long Due)
{
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
        long? Creation, string? Creator, string? Fingerprint);

    /// <summary>Root birth survives retirement, so an admitted occurrence never depends on an expiring ingress receipt.</summary>
    internal async Task<bool> ReadReceiptAsync(KernelDb db, string tenant, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", birth."Reference", birth."Tenant", birth."Actor", birth."Partition",
                birth."Creation", birth."Creator", birth."Fingerprint"
            FROM source LEFT JOIN "FoundationRootBirths" birth ON source."SourceReady" AND birth."Reference" = {Reference}
            """;
        var row = await db.Database.SqlQuery<Receipt>(RootScheduleInventory.Bind(query, bindSource)).SingleAsync(token);
        if (!row.SourceReady) throw new StoreFormatException("The pinned schedule store changed during occurrence admission.");
        if (row.Reference is null) return false;
        if (row.Tenant != tenant || row.Actor != Creator.Target.Id || row.Partition != Creator.Target.Partition || row.Creation != 1 ||
            row.Creator != Creator.InboxKey.Id || row.Fingerprint != Fingerprint)
            throw new StoreFormatException("The scheduled workflow birth differs from its accepted occurrence.");
        return true;
    }

    // This proves retirement of this actor birth. Detached descendants have independent lifetimes.
    internal async Task<bool> IsRetiredAsync(KernelDb db, string tenant, CancellationToken token,
        Func<FormattableString, FormattableString>? bindSource = null)
    {
        if (!await ReadReceiptAsync(db, tenant, token, bindSource)) return false;

        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*schedule-source*/)
            SELECT source."SourceReady", source."SourceReady" AND EXISTS (
                SELECT 1 FROM "Instances" instance
                JOIN "Journal" tombstone ON tombstone."Instance" = instance."Id"
                    AND tombstone."Partition" = instance."Partition" AND tombstone."Revision" = -1
                WHERE instance."Id" = {Creator.Target.Id} AND instance."Partition" = {Creator.Target.Partition}
                    AND instance."FoundationTenant" = {tenant}
                    AND instance."FoundationReference" = {Reference} AND instance."Retired"
                    AND tombstone."Entries" = '["delete"]') AS "Retired"
            FROM source
            """;
        var proof = await db.Database.SqlQuery<RetirementProof>(RootScheduleInventory.Bind(query, bindSource)).SingleAsync(token);
        if (!proof.SourceReady) throw new StoreFormatException("The pinned schedule store changed during retirement confirmation.");
        return proof.Retired;
    }

    private sealed record RetirementProof(bool SourceReady, bool Retired);
}
