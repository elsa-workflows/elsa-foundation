using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// Internal provider seam only. A real family owns decoding, fingerprints, source eligibility and the canonical
// SQL join/fence. It cannot provide an arbitrary commit/JSON/SQL mutation plan. No implementation is registered.
internal abstract class RuntimeDeliveryCanonicalFamilyReader
{
    internal abstract string Family { get; }
    internal abstract IReadOnlyList<RuntimeDeliverySupportedWork> Work { get; }
    internal abstract IQueryable<RuntimeDeliveryWorkEntity> CandidateRows(RuntimeDbContext context,
        RuntimeDeliveryDiscoveryPartition partition, RuntimeDeliverySupportedWork work, long now);
    internal abstract IQueryable<RuntimeDeliveryWorkEntity> EligibleRows(RuntimeDbContext context,
        RuntimeDeliveryDiscoveryPartition partition, RuntimeDeliverySupportedWork work, long now);
    internal abstract ValueTask<RuntimeDeliveryCanonicalRead?> ReadAsync(RuntimeDbContext context,
        RuntimeDeliverySourceAddress address, CancellationToken cancellationToken);
    internal abstract RuntimeDeliveryCanonicalWork ReadWork(RuntimeDeliveryWorkEntity row);
    internal abstract bool IsClaimable(RuntimeDeliveryCanonicalRead source, RuntimeDeliveryCanonicalWork work);
}

internal sealed record RuntimeDeliverySupportedWork(string Kind, RuntimeDeliveryWorkRole Role);
internal sealed record RuntimeDeliveryCanonicalHeader(RuntimeDeliverySourceSnapshot Snapshot,
    RuntimeDeliveryDiscoveryPartition Partition, int Status)
{ public override string ToString() => nameof(RuntimeDeliveryCanonicalHeader); }
internal sealed record RuntimeDeliveryCanonicalWork(RuntimeDeliverySourceAddress Source, string WorkId, string Kind,
    RuntimeDeliveryWorkRole Role, JsonElement Content, string Fingerprint, DateTimeOffset NotBefore)
{ public override string ToString() => nameof(RuntimeDeliveryCanonicalWork); }

internal abstract class RuntimeDeliveryCanonicalRead(RuntimeDeliverySourceSnapshot snapshot,
    RuntimeDeliveryDiscoveryPartition partition)
{
    internal RuntimeDeliverySourceSnapshot Snapshot { get; } = snapshot;
    internal RuntimeDeliveryDiscoveryPartition Partition { get; } = partition;
    // Revision-only CAS on the ACTUAL canonical row, in the caller's provider-owned transaction. Future C2/native
    // implementations must fence their own retained row; they must not manufacture a generic source mirror.
    internal abstract ValueTask StageFenceAsync(RuntimeDbContext context, CancellationToken cancellationToken);
}

// BusinessMessage has no owning codec yet. This narrow seam deliberately does not define its header/cohort policy.
// A future domain implementation must reconstruct these checked values from canonical content and independently
// calculate both fingerprints. Returning projections taken from the row is not an implementation of this contract.
// An adapter may read immutable metadata after payload retirement. Before advertising claimable progress it must
// validate the actual family payload/disposition and source eligibility; missing payload is never invented here.
internal abstract class RuntimeIngressCanonicalCodec
{
    internal abstract int HeaderVersion { get; }
    internal abstract int ParticipantVersion { get; }
    internal abstract IReadOnlyList<int> ClaimableSourceStatuses { get; }
    internal abstract IReadOnlyList<RuntimeDeliverySupportedWork> Work { get; }
    internal abstract RuntimeDeliveryCanonicalHeader ReadHeader(string contentJson);
    internal abstract RuntimeDeliveryCanonicalWork ReadWork(string contentJson);
    // Read-only domain validation, including any required Present-payload binding and work/progress eligibility.
    internal abstract void ValidateClaimable(string contentJson, RuntimeDeliveryCanonicalWork work);
}

internal sealed class RuntimeIngressCanonicalFamilyReader : RuntimeDeliveryCanonicalFamilyReader
{
    internal const string BusinessMessage = "BusinessMessage";
    private readonly RuntimeIngressCanonicalCodec codec;
    private readonly int[] sourceStatuses;
    private readonly int headerVersion;
    private readonly int participantVersion;
    private readonly IReadOnlyList<RuntimeDeliverySupportedWork> work;

    internal RuntimeIngressCanonicalFamilyReader(RuntimeIngressCanonicalCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        this.codec = codec; headerVersion = codec.HeaderVersion; participantVersion = codec.ParticipantVersion;
        sourceStatuses = codec.ClaimableSourceStatuses.ToArray();
        var declarations = codec.Work.ToArray();
        if (headerVersion <= 0 || participantVersion <= 0 || sourceStatuses.Length is 0 or > 32 ||
            sourceStatuses.Distinct().Count() != sourceStatuses.Length || declarations.Length is 0 or > 32 ||
            declarations.Any(item => item is null || string.IsNullOrWhiteSpace(item.Kind) || !Enum.IsDefined(item.Role)) ||
            declarations.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Count() != declarations.Length)
            throw new ArgumentException("A finite exact canonical reader contract is required.", nameof(codec));
        work = Array.AsReadOnly(declarations);
    }

    internal override string Family => BusinessMessage;
    internal override IReadOnlyList<RuntimeDeliverySupportedWork> Work => work;

    internal override IQueryable<RuntimeDeliveryWorkEntity> CandidateRows(RuntimeDbContext context,
        RuntimeDeliveryDiscoveryPartition partition, RuntimeDeliverySupportedWork supported, long now)
    {
        var family = EfRelationalIdentity.Encode(Family); var familyHash = EfRelationalIdentity.Hash(Family);
        var kind = EfRelationalIdentity.Encode(supported.Kind); var kindHash = EfRelationalIdentity.Hash(supported.Kind);
        return RuntimeDeliveryDiscoveryProjection.Create(partition).FilterWork(context.RuntimeDeliveryWorks.AsNoTracking())
            .Where(row => row.SourceFamily == family && row.SourceFamilyHash == familyHash &&
                row.WorkKind == kind && row.WorkKindHash == kindHash && row.ClaimableIsEligible && row.ClaimableAtUtcTicks <= now);
    }

    internal override IQueryable<RuntimeDeliveryWorkEntity> EligibleRows(RuntimeDbContext context,
        RuntimeDeliveryDiscoveryPartition partition, RuntimeDeliverySupportedWork supported, long now)
    {
        var family = EfRelationalIdentity.Encode(Family); var familyHash = EfRelationalIdentity.Hash(Family);
        var version = RuntimeDeliverySourceEfModule.SchemaVersion; var role = (int)supported.Role;
        var sources = RuntimeDeliveryDiscoveryProjection.Create(partition).FilterSources(context.RuntimeIngressSources.AsNoTracking())
            .Where(source => source.SourceFamily == family && source.SourceFamilyHash == familyHash &&
                source.SchemaVersion == version && source.HeaderVersion == headerVersion &&
                source.RequiredParticipantVersion == participantVersion && sourceStatuses.Contains(source.Status));
        var candidates = CandidateRows(context, partition, supported, now).Where(row => row.SchemaVersion == version &&
            row.RequiredParticipantVersion == participantVersion && row.Role == role &&
            (row.Status == RuntimeDeliveryWorkTransport.Pending || row.Status == RuntimeDeliveryWorkTransport.Claimed));
        // The actual canonical source binding, full context, capability and immutable header fence all precede Take.
        return from row in candidates
               join source in sources on new { row.ScopeKey, row.ScopeKeyHash, row.SourceFamily, row.SourceFamilyHash, row.SourceId, row.SourceIdHash }
                   equals new { source.ScopeKey, source.ScopeKeyHash, source.SourceFamily, source.SourceFamilyHash, source.SourceId, source.SourceIdHash }
               where row.SourceHeaderRevision == source.HeaderRevision && row.SourceHeaderFingerprint == source.HeaderFingerprint
               select row;
    }

    internal override async ValueTask<RuntimeDeliveryCanonicalRead?> ReadAsync(RuntimeDbContext context,
        RuntimeDeliverySourceAddress address, CancellationToken cancellationToken)
    {
        if (address.Family != Family) throw new NotSupportedException("The canonical source family is unsupported.");
        var id = EfRelationalIdentity.HashLengthFramed(address.Scope.Value, address.Family, address.SourceId);
        var row = await context.RuntimeIngressSources.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return null;
        EfSchemaVersion.EnsureCurrent(RuntimeDeliverySourceEfModule.Chain, row.SchemaVersion);
        if (row.HeaderVersion != headerVersion || row.RequiredParticipantVersion != participantVersion)
            throw new NotSupportedException("The canonical source protocol is unsupported.");
        var read = codec.ReadHeader(row.ContentJson);
        var source = read.Snapshot;
        RuntimeDeliveryDiscoveryProjection.Validate(row, read.Partition);
        if (source.Address != address || read.Partition.Scope != address.Scope || source.HeaderVersion != headerVersion ||
            source.Revision != row.HeaderRevision || source.HeaderFingerprint != row.HeaderFingerprint ||
            !WorkflowExecutionAuthoritySnapshot.Matches(source.Authority, read.Partition.Authority.SystemIdentity,
                read.Partition.Authority.RootInitiator, read.Partition.Authority.Metadata) ||
            !StringComparer.Ordinal.Equals(source.TenantId, read.Partition.TenantId) || source.RunKind != read.Partition.RunKind ||
            !WorkflowTestScope.ContextEquals(source.TestScope, read.Partition.TestScope) || read.Status != row.Status ||
            row.Revision <= 0 || row.SourceFamily != EfRelationalIdentity.Encode(address.Family) ||
            row.SourceFamilyHash != EfRelationalIdentity.Hash(address.Family) || row.SourceId != EfRelationalIdentity.Encode(address.SourceId) ||
            row.SourceIdHash != EfRelationalIdentity.Hash(address.SourceId))
            throw new InvalidDataException("The canonical source content and projections differ.");
        return new GenericRead(row, source, read.Partition);
    }

    internal override RuntimeDeliveryCanonicalWork ReadWork(RuntimeDeliveryWorkEntity row)
    {
        EfSchemaVersion.EnsureCurrent(RuntimeDeliverySourceEfModule.Chain, row.SchemaVersion);
        if (row.RequiredParticipantVersion != participantVersion)
            throw new NotSupportedException("The canonical work protocol is unsupported.");
        var read = codec.ReadWork(row.ContentJson);
        if (read.Source.Family != Family || !work.Any(item => item.Kind == read.Kind && item.Role == read.Role) ||
            read.Content.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(read.Fingerprint) || read.NotBefore == default ||
            row.Id != EfRelationalIdentity.HashLengthFramed(read.Source.Scope.Value, Family, read.Source.SourceId, read.WorkId) ||
            row.SourceFamily != EfRelationalIdentity.Encode(Family) || row.SourceFamilyHash != EfRelationalIdentity.Hash(Family) ||
            row.SourceId != EfRelationalIdentity.Encode(read.Source.SourceId) || row.SourceIdHash != EfRelationalIdentity.Hash(read.Source.SourceId) ||
            row.WorkId != EfRelationalIdentity.Encode(read.WorkId) || row.WorkIdHash != EfRelationalIdentity.Hash(read.WorkId) ||
            row.WorkKind != EfRelationalIdentity.Encode(read.Kind) || row.WorkKindHash != EfRelationalIdentity.Hash(read.Kind) ||
            row.Role != (int)read.Role || row.ContentFingerprint != read.Fingerprint)
            throw new InvalidDataException("The canonical work content and projections differ.");
        return read with { Content = read.Content.Clone() };
    }

    internal override bool IsClaimable(RuntimeDeliveryCanonicalRead source, RuntimeDeliveryCanonicalWork work)
    {
        if (source is not GenericRead read) throw new InvalidDataException("The canonical source reader differs.");
        if (!sourceStatuses.Contains(read.Status)) return false;
        codec.ValidateClaimable(read.ContentJson, work);
        return true;
    }

    private sealed class GenericRead(RuntimeIngressSourceEntity row, RuntimeDeliverySourceSnapshot snapshot,
        RuntimeDeliveryDiscoveryPartition partition) : RuntimeDeliveryCanonicalRead(snapshot, partition)
    {
        internal int Status => row.Status;
        internal string ContentJson => row.ContentJson;
        internal override async ValueTask StageFenceAsync(RuntimeDbContext context, CancellationToken cancellationToken)
        {
            var revision = checked(row.Revision + 1);
            var count = await context.RuntimeIngressSources.Where(item => item.Id == row.Id && item.Revision == row.Revision &&
                item.SchemaVersion == row.SchemaVersion && item.HeaderRevision == row.HeaderRevision && item.HeaderVersion == row.HeaderVersion &&
                item.RequiredParticipantVersion == row.RequiredParticipantVersion && item.HeaderFingerprint == row.HeaderFingerprint &&
                item.FullDiscoveryIdentity == row.FullDiscoveryIdentity && item.FullDiscoveryIdentityHash == row.FullDiscoveryIdentityHash &&
                item.ScopeKey == row.ScopeKey && item.ScopeKeyHash == row.ScopeKeyHash &&
                item.SourceFamily == row.SourceFamily && item.SourceFamilyHash == row.SourceFamilyHash &&
                item.SourceId == row.SourceId && item.SourceIdHash == row.SourceIdHash &&
                item.TenantId == row.TenantId && item.TenantIdHash == row.TenantIdHash && item.RunKind == row.RunKind &&
                item.TestScopeId == row.TestScopeId && item.TestScopeIdHash == row.TestScopeIdHash && item.AuthorityPartitionKey == row.AuthorityPartitionKey &&
                item.Status == row.Status && item.ContentJson == row.ContentJson)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Revision, revision), cancellationToken);
            if (count != 1) throw new DbUpdateConcurrencyException("The canonical source fence changed.");
        }
    }
}

// PROPOSED internal transport encoding for this unregistered slice. These are owed-work/lease states, not
// Message delivery outcomes. A future typed writer/settler must adopt/version this mapping before participation.
internal static class RuntimeDeliveryWorkTransport
{
    internal const int Pending = 0;
    internal const int Claimed = 1;
}
