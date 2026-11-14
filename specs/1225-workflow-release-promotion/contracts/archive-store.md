# Reviewed frozen release archive store foundation

**Status: Reviewed proposal; SQLite foundation exists on the unmerged historical branch.** T009 prerequisite only. This store preserves a complete
transport capture; it does not create an owner-verified release, prove publisher origin, acquire source retention,
materialize owner catalogs, activate workflows or establish a deployment-attempt journal.
See [bundle](release-bundle.md), [verification](verification.md) and [promotion](promotion.md) for the separate gates.

## Public ports and records

Contracts live in `Elsa.Workflows.Publishing.Releases.Persistence.Core`, referencing existing release transport
Core and [Persistence.Core](../../../Essentials/src/Persistence/Core/SourceRevisions.cs). No EF, Design or source compiler dependency.
The implemented public ports are:

```csharp
public enum ReleaseArchiveCreateOutcome { Unknown = 0, Created = 1, Existing = 2 }
public enum ReleaseArchiveValidationLevel { Unknown = 0, TransportOnly = 1 }

public sealed record ReleaseArchiveCreateReceipt(
    string CaptureId, string ReleaseDigest, ReleaseArchiveCreateOutcome Outcome);

public sealed record ReleaseArchiveStoreCapabilities(
    string Provider, bool DurableCapture, bool SchemaReady);

public interface IReleaseArchiveSnapshot
{
    SourceScope Scope { get; }
    string CaptureId { get; }
    DateTimeOffset CreatedAt { get; }
    ReleaseArchiveValidationLevel ValidationLevel { get; }
    IReleaseBundleTransport Transport { get; }
}

public interface IReleaseArchiveReader
{
    ValueTask<IReleaseArchiveSnapshot?> FindAsync(
        string captureId, CancellationToken cancellationToken = default);
}

public interface IReleaseArchiveWriter
{
    ValueTask<ReleaseArchiveCreateReceipt> CreateAsync(
        string captureId, Stream zip, CancellationToken cancellationToken = default);
}

public interface IReleaseArchiveStoreCapabilities
{
    ValueTask<ReleaseArchiveStoreCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);
}
```

Reader and writer are replacement ports for one selected backend, with scoped implementations. Creation returns a
confirmation, not the archived document. The reader returns an immutable captured transport whose byte getters copy data.
No update/upsert/delete, latest-release selection, cross-scope enumeration or mutable display/evidence methods are introduced.
The sole confirmed validation level is explicitly `TransportOnly`; enum defaults are `Unknown`, never confirmation.
`CaptureId` is an opaque nonblank identifier, at most 256 UTF-16 code units, compared ordinally without normalization.
Typed conflict/store exceptions carry stable safe codes; infrastructure failures retain their original cause internally.
Cancellation before a commit attempt remains cancellation. Failed acknowledgement after commit reached the provider, including
provider cancellation, throws `ReleaseArchiveCommitOutcomeUnknownException`: read/retry the original capture ID to reconcile.
It is never a confirmed failed capture. Diagnostics contain no archive payload or credential-bearing paths.

ZIP is the initial persistence input so the selected `IReleaseBundleCodec.ReadZipAsync` validates manifest, blobs and
attachments together before any write. The stream remains caller-owned. Directory/custom producers can use the existing
codec's ZIP writer; this conversion preserves original manifest/blob/attachment bytes and does not freeze ZIP layout.
Trusted store configuration supplies `ReleaseBundleLimits`; an uploaded bundle cannot enlarge them.

## Trusted scope and identities

Reuse [SourceScope](../../../Essentials/src/Persistence/Core/SourceRevisions.cs): partition, nullable tenant, fixed
`Kind = "workflow-release-archive"`, and authority/environment identity. A server-side registration factory supplies this
scope from authenticated/domain context; no method takes scope from manifest fields or arbitrary request payloads.
Require a nonblank authority and partition; a tenant is either explicitly null or nonblank. A null tenant remains scoped
to the selected ordinary partition and authority; it never grants global or across-partition access.

The EF adapter also requires the existing `IPersistenceAccessContextAccessor.Current.RequireScope()` to equal the supplied
partition. Unbound/global/across-scope or ordinary-operation privileged access fails before queries or mutation.
Tenant and partition remain separate dimensions; do not infer one merely from the other's text.

Derive a lossless length-framed key from authority, partition, explicit null/non-null tenant marker, tenant, kind and
capture ID using existing `EfRelationalIdentity` conventions. Every query includes trusted scope, and every loaded row
must match all original full identity fields after hashed lookup. A hash collision or corrupt identity is a refusal,
never another scope's capture. Caller actor/provenance metadata, if added later, is attribution and grants no access.

`CaptureId` differs from `Transport.Digest`: several captures can contain the same logical release. The digest covers
normalized immutable manifest content; original whitespace, ZIP entry order/compression and attachments are outside it.

## Create, retry and race contract

1. Read and completely validate the bounded ZIP through the selected codec. Preserve its first original manifest bytes,
   every declared blob's exact bytes and the exact attachment path/byte set. No owner semantic or origin claim is added.
2. Capture these values privately before opening the write transaction; do not retain caller-owned mutable arrays/streams.
3. For an existing scoped capture ID, require the same logical release digest, exact blob identity/byte set and exact
   normalized attachment path/byte set. Reordered/whitespace-different equivalent manifests or different ZIP layout are
   an idempotent retry. Return `Existing` and preserve the first manifest representation, attachment bytes and CreatedAt.
4. A different logical digest, blob bytes or attachment set under the same capture ID is `archive_capture_conflict`.
   Never overwrite the first capture or treat a partial/stale representation as a replacement.
5. For a new capture, commit its header, original manifest and all blob/attachment rows in one local transaction.
   A header is readable only with the complete frozen content; readers never return an incomplete prefix.
6. Competing creators race on the unique scoped capture key. The loser rolls back and reads the committed winner using
   fresh owned state, then applies the same equality rules. It returns Existing or conflict without overwriting data.
7. Interruption before commit exposes no capture. A lost response after commit is recovered by the identical capture ID.
   A reader detects missing/corrupt entries, digest/length mismatch or unsupported stored schema and fails visibly.

Persist raw binary manifest/blob/attachment data, not JSONB or reserialized owner JSON. Separate entry rows avoid requiring
one maximum-sized archive in one provider field. Initially entries belong to one capture; cross-capture blob deduplication
and archive retention/deletion are deferred so they cannot introduce an unproved shared-reference GC lifecycle.
Write-once content fields reject tracked mutation. Integrity/schema fields remain explicit and readable independently.
Store an independent SHA-256/raw-byte-length stamp for the first manifest and each entry, including attachments. Read checks
detect formatting-only manifest changes and changed attachment bytes even though the normalized release digest is unchanged.
Before materializing binary values, scoped SQLite length projections validate actual lengths and the complete expanded byte
budget. Bounded payload reads recheck metadata/actual lengths and raw hashes; changed representation visible in the snapshot refuses.
Actual SQLite BLOB storage class is required; TEXT metadata type/length/NUL guards run before returning strings. Encoded
entry names have a separate conservative metadata budget of `MaxExpandedBytes` at four bytes per projected character.
One deferred read snapshot makes metadata sums/preflight/payload consistent; concurrent corruption after its start cannot
grow that read's allocations. The snapshot returns coherent original bytes and the next read refuses changed storage.
These local integrity checks do not prove publisher origin or defend against a trusted database owner replacing all evidence.

## EF adapter and supported composition

Place the adapter in `Publishing/Releases/Persistence/EntityFrameworkCore`; do not reference existing Design-bearing
`Publishing/Persistence/EntityFrameworkCore`. Reuse `Persistence.EntityFramework` primitives and Runtime.Core's trusted
persistence accessor. References to release engine implementations, Design APIs or owner catalog implementations are unnecessary.

Implemented public registration/mapping:

```csharp
ModelBuilder AddReleaseArchiveModel(this ModelBuilder modelBuilder, string providerName);
IServiceCollection AddReleaseArchiveStore<TContext>(
    this IServiceCollection services,
    Func<IServiceProvider, SourceScope> scopeFactory,
    ReleaseBundleLimits? limits = null, bool requireDurable = true) where TContext : DbContext;
```

An ordinary consumer-owned DbContext installs only the archive mappings and owns its connection/schema/migrations.
The initial qualified provider is real SQLite; other providers refuse explicitly until their mapping/store witnesses exist.
Do not advertise a four-provider capability merely because common EF infrastructure supports those provider names.
Store reads/writes use fresh owned context instances from registered options. They neither flush caller-tracked changes
nor enlist an unrelated caller transaction. Scoped factory/accessor state belongs to the store, never a pooled DbContext.
Each owned context uses cloned concrete `DbContextOptions<TContext>` configured with the shared SQLite connection-safety
mitigation; the same safe template supplies `EfSharedTransaction`. Unknown options shapes refuse, and caller options remain
untouched. Consumers sharing that SQLite pool must use the common binding/helper for every opener, as its existing contract requires.
Creation requires file-backed SQLite by default; explicit `requireDurable: false` permits disposable transport consumers.
Capabilities report actual memory/temporary/file mode and full mapped schema readability independently of that option.
The future source builder must require durable capture. Reads/readiness use a qualified SQLite deferred snapshot without reserving writer locks;
restoration validates stored parts directly through `IReleaseBundleCodec.ValidateParts`, preserving original bytes without ZIP recompression.

Declare schema/version and immutable-content metadata through the established EF conventions and check readability on reads.
No registration or store operation calls EnsureCreated/Migrate, applies schema changes or changes schema finalization.
Missing tables/schema are a contextual store failure; operators prepare the host separately. Future built-in module/provider
features must follow normal EfModuleBinding, named persistence resources and schema dormancy rather than adding apply-time setup.

## Qualification of this foundation

Use a real SQLite database and an ordinary consumer context with an unrelated tracked entity. Required witnesses:

- Reopen from a fresh provider/context/process and compare original manifest/blob/attachment bytes and validation level.
- Same capture ID with identical content, equivalent manifest whitespace/property/set order and different ZIP layout
  converges to the first representation. Distinct capture IDs may retain the same logical release digest.
- Different logical content, changed attachment bytes/path set and inconsistent blob/digest data refuse without mutation.
- Two concurrent contexts create the same capture: equal inputs converge, conflicting inputs yield exactly one winner.
- Cross-authority, cross-partition and cross-tenant reads/creates are isolated, including null tenant in an explicit partition.
  Missing/global/across-scope contexts refuse; full stored scope corruption fails closed after hashed-key lookup.
- An injected failure/process interruption between header/entry writes leaves no visible partial capture; retry after commit
  with a lost response returns Existing. These are real transaction/host witnesses, not a mocked store result.
- Successful and failed archive writes leave unrelated caller-tracked changes unsaved; caller transaction ownership is preserved.
- Missing schema refuses without creating tables/upgrading; unsupported providers refuse honestly; cancelled and failed
  outcomes remain distinct from a completed capture. Revalidation refuses persisted missing/corrupt content.

Local focused suite: 44 cases passed, including pooled concurrent create/read/reopen, all required-column readiness regressions, explicit ephemeral opt-in/default refusal,
raw-byte tampering/storage-class/size/NUL metadata refusals, coherent concurrent read snapshots, provider cancellation after
an actual commit, and the killed child-process/uncommitted-reader/reopen witness. This is local
SQLite foundation evidence, not a release/product acceptance result or qualification of other database providers.

## Subsequent source capture and attempt work

This store alone does not implement T009 source capture or source/GC retention. The source builder must hold and qualify
each owner's exact snapshots/material through complete durable archive acceptance, and release those roots only afterward.
Runtime root-write leases do not establish ETL/reusable/source retention automatically or create a cross-database commit fence.
Downloads of a completed capture read this archive, never lazily rebuild from source catalogs.

A future attempt store owns fixed intent, idempotency, journal CAS/lease and observations in separate records. Owner command
guards, effects, compensation settlement, receipts and revocations remain in the owning transactional boundary; storing a
copy of a receipt in the attempt journal does not create serving authority or atomicity across owners.
