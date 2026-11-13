using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

internal sealed class EfReleaseArchiveStore<TContext>(TContext configured, IReleaseBundleCodec codec, SourceScope scope,
    IPersistenceAccessContextAccessor access, ReleaseBundleLimits limits, bool requireDurable)
    : IReleaseArchiveReader, IReleaseArchiveWriter, IReleaseArchiveStoreCapabilities where TContext : DbContext
{
    private const string ArchiveKind = "workflow-release-archive";
    private const int BlobKind = 1;
    private const int AttachmentKind = 2;

    public async ValueTask<ReleaseArchiveStoreCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var identity = Identity("capability-observation");
        await using var context = Fresh();
        var durable = IsFileBacked(context);
        try
        {
            await using var read = await BeginReadSnapshotAsync(context, cancellationToken);
            // A DEFERRED read snapshot never reserves SQLite's writer lock to observe readiness.
            // Reference every mapped column through scalar lengths/comparisons. Readiness never returns
            // archive bytes or unbounded stored metadata strings to the application.
            var headers = await context.Set<ReleaseArchiveRow>().AsNoTracking().Where(x => x.ScopeKey == identity.ScopeKey)
                .Select(x => new { ScopeMatches = x.Authority == identity.Authority && x.Partition == identity.Partition && x.TenantId == identity.Tenant && x.Kind == identity.Kind,
                    KeyLength = x.Key.Length, ScopeLength = x.ScopeKey.Length, CaptureLength = x.CaptureId.Length,
                    ReleaseLength = x.ReleaseDigest.Length, CreatedLength = x.CreatedAt.Length, DigestLength = x.ManifestDigest.Length,
                    x.ManifestByteLength, ActualLength = (long)x.Manifest.Length, x.BlobCount, x.AttachmentCount,
                    VersionMatches = x.SchemaVersion == ReleaseArchiveModelBuilderExtensions.SchemaVersion,
                    StorageType = ArchiveSqliteFunctions.BinaryStorageType(x.Manifest) })
                .Take(1).ToListAsync(cancellationToken);
            var entries = await context.Set<ReleaseArchiveEntryRow>().AsNoTracking().Where(x => x.ScopeKey == identity.ScopeKey)
                .Select(x => new { ScopeMatches = x.Authority == identity.Authority && x.Partition == identity.Partition && x.TenantId == identity.Tenant && x.Kind == identity.Kind,
                    KeyLength = x.Key.Length, ArchiveLength = x.ArchiveKey.Length, ScopeLength = x.ScopeKey.Length, CaptureLength = x.CaptureId.Length,
                    x.EntryKind, NameLength = x.Name.Length, DigestLength = x.ContentDigest.Length, x.ContentByteLength,
                    ActualLength = (long)x.Content.Length, VersionMatches = x.SchemaVersion == ReleaseArchiveModelBuilderExtensions.SchemaVersion,
                    StorageType = ArchiveSqliteFunctions.BinaryStorageType(x.Content) })
                .Take(1).ToListAsync(cancellationToken);
            if (headers.Any(x => !x.ScopeMatches) || entries.Any(x => !x.ScopeMatches))
                throw new ReleaseArchiveStoreException("archive_scope_identity_mismatch");
            var headersReadable = !await context.Set<ReleaseArchiveRow>().AsNoTracking()
                .AnyAsync(x => x.ScopeKey == identity.ScopeKey && x.SchemaVersion != ReleaseArchiveModelBuilderExtensions.SchemaVersion, cancellationToken);
            var entriesReadable = !await context.Set<ReleaseArchiveEntryRow>().AsNoTracking()
                .AnyAsync(x => x.ScopeKey == identity.ScopeKey && x.SchemaVersion != ReleaseArchiveModelBuilderExtensions.SchemaVersion, cancellationToken);
            return new ReleaseArchiveStoreCapabilities(EfProviderNames.Sqlite, durable, headersReadable && entriesReadable);
        }
        catch (OperationCanceledException) { throw; }
        catch (DbException) { return new ReleaseArchiveStoreCapabilities(EfProviderNames.Sqlite, durable, false); }
    }

    public async ValueTask<IReleaseArchiveSnapshot?> FindAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var identity = Identity(captureId);
        try
        {
            await using var context = Fresh();
            await using var read = await BeginReadSnapshotAsync(context, cancellationToken);
            return await LoadAsync(context, identity, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ReleaseArchiveStoreException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    public async ValueTask<ReleaseArchiveCreateReceipt> CreateAsync(string captureId, Stream zip, CancellationToken cancellationToken = default)
    {
        var identity = Identity(captureId);
        ArgumentNullException.ThrowIfNull(zip);
        if (requireDurable && !IsFileBacked(configured)) throw new ReleaseArchiveStoreException("archive_durable_store_required");
        // The codec captures independent bounded byte arrays before a database transaction begins.
        var incoming = await codec.ReadZipAsync(zip, limits, cancellationToken);
        var metadataUnits = incoming.AttachmentPaths.Sum(EncodedLength) +
            incoming.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal).Sum(EncodedLength);
        if (metadataUnits > limits.MaxExpandedBytes / 4) throw new ReleaseArchiveStoreException("archive_metadata_limit_exceeded");
        var existing = await FindAsync(captureId, cancellationToken);
        if (existing is not null) return ConfirmExisting(existing, incoming);

        try
        {
            // The shared transaction clones this already-safe template, never the caller's options/context.
            await using var template = Fresh();
            await using var transaction = await EfSharedTransaction.BeginAsync([template], cancellationToken);
            var context = transaction.Context<TContext>();
            // A competing writer may have committed while this writer waited for the SQLite transaction.
            existing = await LoadAsync(context, identity, cancellationToken);
            if (existing is not null) return ConfirmExisting(existing, incoming);

            var header = Header(identity, incoming);
            context.Set<ReleaseArchiveRow>().Add(header);
            await context.SaveChangesAsync(cancellationToken);
            context.Set<ReleaseArchiveEntryRow>().AddRange(Entries(identity, incoming));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReleaseArchiveCreateReceipt(captureId, incoming.Digest, ReleaseArchiveCreateOutcome.Created);
        }
        catch (EfCommitOutcomeUnknownException exception)
        {
            // This includes provider cancellation after commit was attempted. It is never a confirmed failed capture.
            throw new ReleaseArchiveCommitOutcomeUnknownException(exception);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            // The transaction's owned context has been disposed and rolled back before this fresh read.
            existing = await FindAsync(captureId, cancellationToken);
            if (existing is null) throw new ReleaseArchiveStoreException("archive_identity_conflict", exception);
            return ConfirmExisting(existing, incoming);
        }
        catch (OperationCanceledException) { throw; }
        catch (ReleaseArchiveStoreException) { throw; }
        catch (Exception exception) { throw Failure(exception); }
    }

    private async Task<IReleaseArchiveSnapshot?> LoadAsync(DbContext context, ArchiveIdentity identity, CancellationToken cancellationToken)
    {
        var scopedHeaders = context.Set<ReleaseArchiveRow>().AsNoTracking().Where(x => x.Key == identity.ArchiveKey && x.ScopeKey == identity.ScopeKey);
        var identityHeaders = scopedHeaders.Where(x => x.Authority == identity.Authority && x.Partition == identity.Partition &&
            x.TenantId == identity.Tenant && x.Kind == identity.Kind && x.CaptureId == identity.EncodedCaptureId);
        // Bounded metadata predicates execute before any TEXT value is returned. SQLite length(TEXT) stops at NUL;
        // refusing embedded NUL and non-TEXT storage makes the character bound also bound UTF-16 materialization.
        var safeHeaders = identityHeaders.Where(x => ArchiveSqliteFunctions.TextStorageType(x.ReleaseDigest) == "text" &&
            x.ReleaseDigest.Length == 71 && !x.ReleaseDigest.Contains(ArchiveSqliteFunctions.Character(0)) && ArchiveSqliteFunctions.TextStorageType(x.CreatedAt) == "text" &&
            x.CreatedAt.Length <= 64 && !x.CreatedAt.Contains(ArchiveSqliteFunctions.Character(0)) && ArchiveSqliteFunctions.TextStorageType(x.ManifestDigest) == "text" &&
            x.ManifestDigest.Length == 64 && !x.ManifestDigest.Contains(ArchiveSqliteFunctions.Character(0)) && x.SchemaVersion == ReleaseArchiveModelBuilderExtensions.SchemaVersion &&
            ArchiveSqliteFunctions.BinaryStorageType(x.Manifest) == "blob");
        var header = await safeHeaders
            .Select(x => new HeaderMetadata(x.Key, x.ScopeKey, x.Authority, x.Partition, x.TenantId, x.Kind, x.CaptureId,
                x.ReleaseDigest, x.CreatedAt, x.ManifestDigest, x.ManifestByteLength, (long)x.Manifest.Length,
                x.BlobCount, x.AttachmentCount, x.SchemaVersion))
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null)
        {
            if (!await scopedHeaders.AnyAsync(cancellationToken)) return null;
            if (!await identityHeaders.AnyAsync(cancellationToken)) throw new ReleaseArchiveStoreException("archive_scope_identity_mismatch");
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        }
        RequireIdentity(identity, header.Authority, header.Partition, header.TenantId, header.Kind, header.CaptureId);
        if (header.SchemaVersion != ReleaseArchiveModelBuilderExtensions.SchemaVersion || header.BlobCount < 0 || header.AttachmentCount < 0 ||
            header.BlobCount > limits.MaxEntries || header.AttachmentCount > limits.MaxEntries ||
            (long)header.BlobCount + header.AttachmentCount + 1 > limits.MaxEntries)
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        RequireLength(header.ManifestByteLength, header.ActualManifestLength, limits.MaxManifestBytes, header.ManifestDigest);
        long expanded = header.ActualManifestLength;
        if (expanded > limits.MaxExpandedBytes) throw new ReleaseArchiveStoreException("archive_stored_content_invalid");

        // SQLite translates byte[].Length to length(BLOB). No payload bytes are materialized until every
        // actual stored length and the total expanded budget have passed this metadata preflight.
        var scopedEntries = context.Set<ReleaseArchiveEntryRow>().AsNoTracking().Where(x => x.ArchiveKey == identity.ArchiveKey && x.ScopeKey == identity.ScopeKey);
        var entryCount = await scopedEntries.LongCountAsync(cancellationToken);
        if (entryCount != (long)header.BlobCount + header.AttachmentCount) throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        var identityEntries = scopedEntries.Where(x => x.Authority == identity.Authority && x.Partition == identity.Partition &&
            x.TenantId == identity.Tenant && x.Kind == identity.Kind && x.CaptureId == identity.EncodedCaptureId);
        if (await identityEntries.LongCountAsync(cancellationToken) != entryCount) throw new ReleaseArchiveStoreException("archive_scope_identity_mismatch");
        var safeEntries = identityEntries.Where(x => ArchiveSqliteFunctions.TextStorageType(x.Key) == "text" && x.Key.Length == 64 && !x.Key.Contains(ArchiveSqliteFunctions.Character(0)) &&
            ArchiveSqliteFunctions.TextStorageType(x.Name) == "text" && x.Name.Length <= limits.MaxExpandedBytes / 4 && !x.Name.Contains(ArchiveSqliteFunctions.Character(0)) &&
            ArchiveSqliteFunctions.TextStorageType(x.ContentDigest) == "text" && x.ContentDigest.Length == 64 && !x.ContentDigest.Contains(ArchiveSqliteFunctions.Character(0)) &&
            x.SchemaVersion == ReleaseArchiveModelBuilderExtensions.SchemaVersion && ArchiveSqliteFunctions.BinaryStorageType(x.Content) == "blob");
        if (await safeEntries.LongCountAsync(cancellationToken) != entryCount ||
            await safeEntries.SumAsync(x => (long)x.Name.Length, cancellationToken) > limits.MaxExpandedBytes / 4)
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        var rows = await safeEntries
            .Select(x => new EntryMetadata(x.Key, x.ArchiveKey, x.ScopeKey, x.Authority, x.Partition, x.TenantId, x.Kind,
                x.CaptureId, x.EntryKind, x.Name, x.ContentDigest, x.ContentByteLength, (long)x.Content.Length, x.SchemaVersion))
            .Take(limits.MaxEntries).ToListAsync(cancellationToken);
        var blobs = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var attachments = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var names = new HashSet<(int Kind, string Name)>();
        foreach (var entry in rows)
        {
            RequireIdentity(identity, entry.Authority, entry.Partition, entry.TenantId, entry.Kind, entry.CaptureId);
            var name = EfRelationalIdentity.Decode(entry.Name);
            if (entry.SchemaVersion != ReleaseArchiveModelBuilderExtensions.SchemaVersion ||
                entry.Key != EntryKey(identity, entry.EntryKind, name) || entry.EntryKind is not (BlobKind or AttachmentKind) ||
                !names.Add((entry.EntryKind, name)))
                throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
            RequireLength(entry.ContentByteLength, entry.ActualContentLength, limits.MaxBlobBytes, entry.ContentDigest);
            if (entry.ActualContentLength > limits.MaxExpandedBytes - expanded)
                throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
            expanded += entry.ActualContentLength;
        }
        if (rows.Count(x => x.EntryKind == BlobKind) != header.BlobCount || rows.Count(x => x.EntryKind == AttachmentKind) != header.AttachmentCount)
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");

        // Recheck exact metadata and actual bounded length in SQL before fetching each payload. A changed
        // row cannot grow an allocation after the preflight or silently substitute another stored representation.
        var manifest = await context.Set<ReleaseArchiveRow>().AsNoTracking().Where(x => x.Key == identity.ArchiveKey && x.ScopeKey == identity.ScopeKey &&
            x.Authority == header.Authority && x.Partition == header.Partition && x.TenantId == header.TenantId && x.Kind == header.Kind &&
            x.CaptureId == header.CaptureId && x.ReleaseDigest == header.ReleaseDigest && x.CreatedAt == header.CreatedAt &&
            x.ManifestDigest == header.ManifestDigest && x.ManifestByteLength == header.ManifestByteLength && (long)x.Manifest.Length == header.ActualManifestLength &&
            ArchiveSqliteFunctions.BinaryStorageType(x.Manifest) == "blob" &&
            x.BlobCount == header.BlobCount && x.AttachmentCount == header.AttachmentCount && x.SchemaVersion == header.SchemaVersion)
            .Select(x => x.Manifest).SingleOrDefaultAsync(cancellationToken);
        if (manifest is null || Hash(manifest) != header.ManifestDigest) throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        foreach (var entry in rows)
        {
            var bytes = await context.Set<ReleaseArchiveEntryRow>().AsNoTracking().Where(x => x.Key == entry.Key && x.ArchiveKey == identity.ArchiveKey &&
                x.ScopeKey == identity.ScopeKey && x.Authority == entry.Authority && x.Partition == entry.Partition && x.TenantId == entry.TenantId &&
                x.Kind == entry.Kind && x.CaptureId == entry.CaptureId && x.EntryKind == entry.EntryKind && x.Name == entry.Name &&
                x.ContentDigest == entry.ContentDigest && x.ContentByteLength == entry.ContentByteLength && (long)x.Content.Length == entry.ActualContentLength &&
                ArchiveSqliteFunctions.BinaryStorageType(x.Content) == "blob" &&
                x.SchemaVersion == entry.SchemaVersion).Select(x => x.Content).SingleOrDefaultAsync(cancellationToken);
            if (bytes is null || Hash(bytes) != entry.ContentDigest) throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
            var collection = entry.EntryKind switch
            {
                BlobKind => blobs,
                AttachmentKind => attachments,
                _ => throw new ReleaseArchiveStoreException("archive_stored_content_invalid")
            };
            collection.Add(EfRelationalIdentity.Decode(entry.Name), bytes);
        }
        IReleaseBundleTransport transport;
        try { transport = codec.ValidateParts(manifest, blobs, attachments, limits); }
        catch (ReleaseBundleFormatException exception) { throw new ReleaseArchiveStoreException("archive_stored_content_invalid", exception); }
        if (transport.Digest != header.ReleaseDigest ||
            !DateTimeOffset.TryParseExact(header.CreatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt))
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
        return new ArchiveSnapshot(scope, identity.CaptureId, createdAt, transport);
    }

    private ReleaseArchiveCreateReceipt ConfirmExisting(IReleaseArchiveSnapshot existing, IReleaseBundleTransport incoming)
    {
        var original = existing.Transport;
        var blobNames = original.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var incomingNames = incoming.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (original.Digest != incoming.Digest || !blobNames.SequenceEqual(incomingNames, StringComparer.Ordinal) ||
            !original.AttachmentPaths.Order(StringComparer.Ordinal).SequenceEqual(incoming.AttachmentPaths.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            blobNames.Any(name => !original.GetBlobBytes(name).AsSpan().SequenceEqual(incoming.GetBlobBytes(name))) ||
            original.AttachmentPaths.Any(name => !original.GetAttachmentBytes(name).AsSpan().SequenceEqual(incoming.GetAttachmentBytes(name))))
            throw new ReleaseArchiveStoreException("archive_capture_conflict");
        return new ReleaseArchiveCreateReceipt(existing.CaptureId, original.Digest, ReleaseArchiveCreateOutcome.Existing);
    }

    private ArchiveIdentity Identity(string captureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureId);
        if (captureId.Length > 256) throw new ArgumentOutOfRangeException(nameof(captureId));
        if (scope is null || string.IsNullOrWhiteSpace(scope.Authority) || string.IsNullOrWhiteSpace(scope.Partition) ||
            scope.Kind != ArchiveKind || scope.TenantId is not null && string.IsNullOrWhiteSpace(scope.TenantId))
            throw new ReleaseArchiveStoreException("archive_scope_invalid");
        try
        {
            if (!StringComparer.Ordinal.Equals(access.Current.RequireScope().Value, scope.Partition))
                throw new ReleaseArchiveStoreException("archive_scope_invalid");
        }
        catch (InvalidOperationException exception) { throw new ReleaseArchiveStoreException("archive_scope_invalid", exception); }
        ReleaseArchiveModelBuilderExtensions.EnsureProvider(configured.Database.ProviderName);
        _ = RequireIndependentOptions();
        var scopeKey = EfRelationalIdentity.HashLengthFramed(scope.Authority, scope.Partition,
            scope.TenantId is null ? "null" : "tenant", scope.TenantId ?? "", scope.Kind);
        return new ArchiveIdentity(scopeKey, EfRelationalIdentity.HashLengthFramed(scopeKey, captureId), captureId,
            EfRelationalIdentity.Encode(scope.Authority), EfRelationalIdentity.Encode(scope.Partition),
            scope.TenantId is null ? null : EfRelationalIdentity.Encode(scope.TenantId), EfRelationalIdentity.Encode(scope.Kind), EfRelationalIdentity.Encode(captureId));
    }

    private TContext Fresh()
    {
        var builder = new DbContextOptionsBuilder<TContext>(RequireIndependentOptions());
        EfRelationalProviderBinding.ConfigureSqliteConnectionSafety(builder);
        var constructor = typeof(TContext).GetConstructor([typeof(DbContextOptions<TContext>)]) ??
            throw new ReleaseArchiveStoreException("archive_context_constructor_invalid");
        return (TContext)constructor.Invoke([builder.Options]);
    }

    private DbContextOptions<TContext> RequireIndependentOptions()
    {
        if (configured.GetType() != typeof(TContext) || configured.GetService<IDbContextOptions>() is not DbContextOptions<TContext> options)
            throw new ReleaseArchiveStoreException("archive_context_options_unsupported");
        // An explicit connection would share the caller's transaction and disposal ownership.
        if (RelationalOptionsExtension.Extract(options).Connection is not null)
            throw new ReleaseArchiveStoreException("archive_shared_connection_unsupported");
        return options;
    }

    private static async Task<ReadSnapshot> BeginReadSnapshotAsync(DbContext context, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        // The qualified SQLite shape is explicit; its default immediate transaction would reserve the writer lock.
        if (connection is not SqliteConnection sqlite) throw new ReleaseArchiveStoreException("archive_read_snapshot_unsupported");
        var physical = sqlite.BeginTransaction(deferred: true);
        try
        {
            var enlisted = await context.Database.UseTransactionAsync(physical, cancellationToken) ??
                throw new ReleaseArchiveStoreException("archive_read_snapshot_unsupported");
            return new ReadSnapshot(enlisted, physical);
        }
        catch { await physical.DisposeAsync(); throw; }
    }

    private static void RequireIdentity(ArchiveIdentity expected, string authority, string partition, string? tenant, string kind, string capture)
    {
        RequireScopeIdentity(expected, authority, partition, tenant, kind);
        if (capture != expected.EncodedCaptureId)
            throw new ReleaseArchiveStoreException("archive_scope_identity_mismatch");
    }

    private static void RequireScopeIdentity(ArchiveIdentity expected, string authority, string partition, string? tenant, string kind)
    {
        if (authority != expected.Authority || partition != expected.Partition || tenant != expected.Tenant || kind != expected.Kind)
            throw new ReleaseArchiveStoreException("archive_scope_identity_mismatch");
    }

    private static ReleaseArchiveRow Header(ArchiveIdentity identity, IReleaseBundleTransport transport)
    {
        var manifest = transport.GetManifestBytes();
        return new ReleaseArchiveRow
        {
            Key = identity.ArchiveKey, ScopeKey = identity.ScopeKey, Authority = identity.Authority, Partition = identity.Partition,
            TenantId = identity.Tenant, Kind = identity.Kind, CaptureId = identity.EncodedCaptureId, ReleaseDigest = transport.Digest,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), Manifest = manifest,
            ManifestDigest = Hash(manifest), ManifestByteLength = manifest.LongLength,
            BlobCount = transport.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal).Count(), AttachmentCount = transport.AttachmentPaths.Count
        };
    }

    private static IEnumerable<ReleaseArchiveEntryRow> Entries(ArchiveIdentity identity, IReleaseBundleTransport transport)
    {
        foreach (var digest in transport.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal))
            yield return Entry(identity, BlobKind, digest, transport.GetBlobBytes(digest));
        foreach (var path in transport.AttachmentPaths)
            yield return Entry(identity, AttachmentKind, path, transport.GetAttachmentBytes(path));
    }

    private static ReleaseArchiveEntryRow Entry(ArchiveIdentity identity, int kind, string name, byte[] content) => new()
    {
        Key = EntryKey(identity, kind, name), ArchiveKey = identity.ArchiveKey, ScopeKey = identity.ScopeKey,
        Authority = identity.Authority, Partition = identity.Partition, TenantId = identity.Tenant,
        Kind = identity.Kind, CaptureId = identity.EncodedCaptureId, EntryKind = kind, Name = EfRelationalIdentity.Encode(name), Content = content,
        ContentDigest = Hash(content), ContentByteLength = content.LongLength
    };

    private static string EntryKey(ArchiveIdentity identity, int kind, string name) =>
        EfRelationalIdentity.HashLengthFramed(identity.ArchiveKey, kind.ToString(CultureInfo.InvariantCulture), name);

    private static ReleaseArchiveStoreException Failure(Exception cause) => new("archive_storage_unavailable", cause);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static long EncodedLength(string value) => ((long)value.Length * 2 + 2) / 3 * 4;

    private static void RequireLength(long declared, long actual, long maximum, string digest)
    {
        if (actual < 0 || declared != actual || actual > maximum || digest is null || digest.Length != 64 ||
            digest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ReleaseArchiveStoreException("archive_stored_content_invalid");
    }

    private static bool IsFileBacked(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var builder = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
        var source = new[] { "Data Source", "DataSource", "Filename" }
            .Select(key => builder.TryGetValue(key, out var value) ? value?.ToString() : null).FirstOrDefault(value => value is not null) ?? connection.DataSource;
        var memory = builder.TryGetValue("Mode", out var mode) && string.Equals(mode?.ToString(), "Memory", StringComparison.OrdinalIgnoreCase);
        var path = source;
        if (source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = source[5..].Split('#', 2)[0].Split('?', 2);
            path = Uri.UnescapeDataString(parts[0]);
            if (path.StartsWith("//", StringComparison.Ordinal))
            {
                var slash = path.IndexOf('/', 2);
                path = slash < 0 ? "" : path[slash..];
            }
            if (parts.Length == 2)
                foreach (var parameter in parts[1].Split('&'))
                {
                    var pair = parameter.Split('=', 2);
                    var key = Uri.UnescapeDataString(pair[0]);
                    var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "";
                    memory |= key.Equals("mode", StringComparison.OrdinalIgnoreCase) && value.Equals("memory", StringComparison.OrdinalIgnoreCase);
                    memory |= key.Equals("vfs", StringComparison.OrdinalIgnoreCase) || key.Contains('\0') || value.Contains('\0');
                }
        }
        return !string.IsNullOrWhiteSpace(path) && !path.Equals(":memory:", StringComparison.OrdinalIgnoreCase) && !path.Contains('\0') && !memory;
    }

    private sealed record ArchiveIdentity(string ScopeKey, string ArchiveKey, string CaptureId,
        string Authority, string Partition, string? Tenant, string Kind, string EncodedCaptureId);

    private sealed record HeaderMetadata(string Key, string ScopeKey, string Authority, string Partition, string? TenantId, string Kind,
        string CaptureId, string ReleaseDigest, string CreatedAt, string ManifestDigest, long ManifestByteLength,
        long ActualManifestLength, int BlobCount, int AttachmentCount, string SchemaVersion);

    private sealed record EntryMetadata(string Key, string ArchiveKey, string ScopeKey, string Authority, string Partition, string? TenantId,
        string Kind, string CaptureId, int EntryKind, string Name, string ContentDigest, long ContentByteLength, long ActualContentLength, string SchemaVersion);

    private sealed class ReadSnapshot(IDbContextTransaction enlisted, DbTransaction physical) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await enlisted.DisposeAsync(); }
            finally { await physical.DisposeAsync(); }
        }
    }

    private sealed class ArchiveSnapshot(SourceScope scope, string captureId, DateTimeOffset createdAt, IReleaseBundleTransport transport) : IReleaseArchiveSnapshot
    {
        public SourceScope Scope { get; } = scope;
        public string CaptureId { get; } = captureId;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public ReleaseArchiveValidationLevel ValidationLevel => ReleaseArchiveValidationLevel.TransportOnly;
        public IReleaseBundleTransport Transport { get; } = transport;
    }
}
