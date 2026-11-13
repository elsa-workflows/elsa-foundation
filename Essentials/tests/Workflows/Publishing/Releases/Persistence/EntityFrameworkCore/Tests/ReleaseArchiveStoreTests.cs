using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests;

public sealed class ReleaseArchiveStoreTests
{
    [Fact]
    public async Task Reopen_preserves_original_binary_parts_and_does_not_recompress_for_archive_limit()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var fixture = new ArchiveFixture();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
        {
            var capabilities = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync();
            Assert.True(capabilities.DurableCapture);
            Assert.True(capabilities.SchemaReady);
            using var input = new MemoryStream(fixture.Zip());
            var result = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("capture-not-a-digest", input);
            Assert.Equal(ReleaseArchiveCreateOutcome.Created, result.Outcome);
            Assert.NotEqual(result.CaptureId, result.ReleaseDigest);
            Assert.True(input.CanRead);
        }
        await using var reopened = database.Services(limits: new ReleaseBundleLimits { MaxArchiveBytes = 1 });
        await using var readScope = reopened.CreateAsyncScope();
        var capture = await readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("capture-not-a-digest");
        Assert.NotNull(capture);
        Assert.Equal(ArchiveFixture.DefaultScope, capture.Scope);
        Assert.Equal(ReleaseArchiveValidationLevel.TransportOnly, capture.ValidationLevel);
        Assert.Equal(fixture.ManifestBytes, capture.Transport.GetManifestBytes());
        Assert.Equal(fixture.Payload, capture.Transport.GetBlobBytes(fixture.BlobDigest));
        Assert.Equal(fixture.Attachment, capture.Transport.GetAttachmentBytes("attachments/receipt.bin"));
        var mutable = capture.Transport.GetManifestBytes(); mutable[0] = 0;
        Assert.Equal(fixture.ManifestBytes, capture.Transport.GetManifestBytes());
    }

    [Fact]
    public async Task Logical_equal_retry_recovers_first_manifest_despite_whitespace_property_set_and_zip_order()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var fixture = new ArchiveFixture();
        var members = (JsonArray)fixture.Manifest["members"]!;
        var second = members[0]!.DeepClone(); second["key"]!["identity"] = "peer";
        members.Add(second);
        ((JsonArray)fixture.Manifest["edges"]!).Add(new JsonObject
        {
            ["from"] = members[0]!["key"]!.DeepClone(), ["to"] = second["key"]!.DeepClone(),
            ["relation"] = "dependency", ["occurrence"] = "node"
        });
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var original = fixture.ManifestBytes;
        var first = await writer.CreateAsync("same-capture", new MemoryStream(fixture.Zip()));
        var before = (await reader.FindAsync("same-capture"))!;
        members.Clear(); members.Add(second.DeepClone());
        var firstMember = JsonNode.Parse(original)!["members"]![0]!.DeepClone(); members.Add(firstMember);
        var retry = await writer.CreateAsync("same-capture", new MemoryStream(fixture.Zip(alternateFormat: true)));
        var after = (await reader.FindAsync("same-capture"))!;
        Assert.Equal(ReleaseArchiveCreateOutcome.Existing, retry.Outcome);
        Assert.Equal(first.ReleaseDigest, retry.ReleaseDigest);
        Assert.Equal(original, after.Transport.GetManifestBytes());
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(ReleaseArchiveCreateOutcome.Created,
            (await writer.CreateAsync("another-capture", new MemoryStream(fixture.Zip(alternateFormat: true)))).Outcome);
    }

    [Fact]
    public async Task Conflicting_content_and_attachments_refuse_without_overwriting_and_invalid_blob_never_writes()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var fixture = new ArchiveFixture();
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        await writer.CreateAsync("capture", new MemoryStream(fixture.Zip()));
        foreach (var changed in new[] { new ArchiveFixture(2).Zip(), fixture.Zip(attachment: [1, 2]), fixture.Zip(attachmentPath: "attachments/changed.bin") })
            Assert.Equal("archive_capture_conflict", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(
                () => writer.CreateAsync("capture", new MemoryStream(changed)).AsTask())).Code);
        await Assert.ThrowsAsync<ReleaseBundleFormatException>(() => writer.CreateAsync("bad-blob", new MemoryStream(fixture.Zip(blobOverride: "{}"u8.ToArray()))).AsTask());
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        Assert.Null(await reader.FindAsync("bad-blob"));
        Assert.Equal(fixture.ManifestBytes, (await reader.FindAsync("capture"))!.Transport.GetManifestBytes());
        Assert.Equal(ReleaseArchiveCreateOutcome.Unknown, default);
    }

    [Fact]
    public async Task Full_authority_partition_tenant_scope_isolated_including_explicit_null_tenant()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var selected = ArchiveFixture.DefaultScope;
        SourceScope[] scopes = [selected, selected with { Authority = "other-environment" }, selected with { Partition = "other-partition" },
            selected with { TenantId = "other-tenant" }, selected with { TenantId = null }];
        for (var index = 0; index < scopes.Length; index++)
        {
            await using var services = database.Services(scopes[index]);
            await using var scope = services.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
            Assert.Null(await reader.FindAsync("shared-capture-id"));
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("shared-capture-id", new MemoryStream(new ArchiveFixture(index).Zip()));
            Assert.Equal(scopes[index], (await reader.FindAsync("shared-capture-id"))!.Scope);
        }
        for (var index = 0; index < scopes.Length; index++)
        {
            await using var services = database.Services(scopes[index]);
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(new ArchiveFixture(index).Payload,
                (await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("shared-capture-id"))!.Transport
                .GetBlobBytes(new ArchiveFixture(index).BlobDigest));
        }
    }

    [Fact]
    public async Task Scope_is_checked_for_each_operation_and_global_privileged_across_or_mismatched_access_refuses()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var access = new MutableAccess(PersistenceAccessContext.Global);
        await using var services = database.Services(access: access);
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        var capabilities = scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>();
        var purpose = new PersistenceAccessPurpose("test-maintenance");
        foreach (var invalid in new[] { PersistenceAccessContext.Global,
                     PersistenceAccessContext.Scoped(new PersistenceScope("different")),
                     PersistenceAccessContext.PrivilegedAcrossScopes(purpose),
                     PersistenceAccessContext.PrivilegedScoped(new PersistenceScope(ArchiveFixture.DefaultScope.Partition), purpose) })
        {
            access.Current = invalid;
            Assert.Equal("archive_scope_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => reader.FindAsync("capture").AsTask())).Code);
            await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => writer.CreateAsync("capture", new MemoryStream(new ArchiveFixture().Zip())).AsTask());
            await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => capabilities.GetCapabilitiesAsync().AsTask());
        }
        access.Current = PersistenceAccessContext.Scoped(new PersistenceScope(ArchiveFixture.DefaultScope.Partition));
        Assert.Null(await reader.FindAsync("capture"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_creators_converge_or_report_exactly_one_conflict(bool conflicting)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(ReleaseArchiveCreateReceipt? Receipt, ReleaseArchiveStoreException? Failure)> Create(int value)
        {
            await using var services = database.Services();
            await using var scope = services.CreateAsyncScope();
            await start.Task;
            try { return (await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("race", new MemoryStream(new ArchiveFixture(value).Zip())), null); }
            catch (ReleaseArchiveStoreException exception) { return (null, exception); }
        }
        var first = Task.Run(() => Create(1));
        var second = Task.Run(() => Create(conflicting ? 2 : 1));
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(outcomes, x => x.Receipt?.Outcome == ReleaseArchiveCreateOutcome.Created);
        if (conflicting) Assert.Equal("archive_capture_conflict", Assert.Single(outcomes, x => x.Failure is not null).Failure!.Code);
        else Assert.Single(outcomes, x => x.Receipt?.Outcome == ReleaseArchiveCreateOutcome.Existing);
    }

    [Fact]
    public async Task Pooled_consumer_options_support_concurrent_create_read_and_reopen_with_owned_connection_safety()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var connectionString = "Data Source=" + database.Path + ";Pooling=True;Default Timeout=10";
        await using var services = ArchiveFixture.Services(connectionString);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new ArchiveFixture();
        var tasks = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            await start.Task;
            var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
            var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
            var shared = await writer.CreateAsync("pooled-shared", new MemoryStream(fixture.Zip()));
            Assert.Equal(fixture.Payload, (await reader.FindAsync("pooled-shared"))!.Transport.GetBlobBytes(fixture.BlobDigest));
            await writer.CreateAsync("pooled-" + index, new MemoryStream(fixture.Zip()));
            Assert.Equal(fixture.ManifestBytes, (await reader.FindAsync("pooled-" + index))!.Transport.GetManifestBytes());
            return shared;
        })).ToArray();
        start.SetResult();
        var receipts = await Task.WhenAll(tasks);
        Assert.Single(receipts, x => x.Outcome == ReleaseArchiveCreateOutcome.Created);
        Assert.Equal(7, receipts.Count(x => x.Outcome == ReleaseArchiveCreateOutcome.Existing));
        await using var reopened = ArchiveFixture.Services(connectionString);
        await using var readScope = reopened.CreateAsyncScope();
        var restored = readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        for (var index = 0; index < tasks.Length; index++) Assert.NotNull(await restored.FindAsync("pooled-" + index));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Failure_after_header_rolls_back_and_success_and_failure_leave_caller_tracked_data_unsaved()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var failure = new FailAfterHeader();
        await using var services = database.Services(interceptor: failure);
        await using var scope = services.CreateAsyncScope();
        var caller = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
        caller.Set<UnrelatedItem>().Add(new UnrelatedItem { Id = "caller-only" });
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        failure.Armed = true;
        await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => writer.CreateAsync("failed", new MemoryStream(new ArchiveFixture().Zip())).AsTask());
        Assert.True(caller.ChangeTracker.HasChanges());
        failure.Armed = false;
        await writer.CreateAsync("success", new MemoryStream(new ArchiveFixture().Zip()));
        Assert.True(caller.ChangeTracker.HasChanges());
        await using var observerServices = database.Services();
        await using var observerScope = observerServices.CreateAsyncScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ConsumerContext>();
        Assert.Empty(await observer.Set<UnrelatedItem>().ToListAsync());
        Assert.Null(await observerScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("failed"));
        Assert.Single(await observer.Set<ReleaseArchiveRow>().ToListAsync());
    }

    [Fact]
    public async Task Owned_write_does_not_enlist_or_complete_callers_deferred_transaction()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var caller = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
        await caller.Database.OpenConnectionAsync();
        await using var physical = ((SqliteConnection)caller.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await caller.Database.UseTransactionAsync(physical);
        caller.Set<UnrelatedItem>().Add(new UnrelatedItem { Id = "caller" });
        await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("independent", new MemoryStream(new ArchiveFixture().Zip()));
        Assert.Same(transaction, caller.Database.CurrentTransaction);
        Assert.True(caller.ChangeTracker.HasChanges());
        await physical.RollbackAsync();
    }

    [Fact]
    public async Task Provider_cancellation_after_commit_is_ambiguous_and_same_capture_read_retry_recovers()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services(interceptor: new LoseCommitAcknowledgement());
        await using var scope = services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        var error = await Assert.ThrowsAsync<ReleaseArchiveCommitOutcomeUnknownException>(
            () => writer.CreateAsync("commit-capture", new MemoryStream(new ArchiveFixture().Zip())).AsTask());
        Assert.Equal("archive_commit_outcome_unknown", error.Code);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("commit-capture"));
        Assert.Equal(ReleaseArchiveCreateOutcome.Existing,
            (await writer.CreateAsync("commit-capture", new MemoryStream(new ArchiveFixture().Zip(alternateFormat: true)))).Outcome);
    }

    [Fact]
    public async Task Cancellation_after_header_before_commit_remains_cancellation_and_exposes_no_capture()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await using var services = database.Services(interceptor: new CancelAfterHeader(cancellation));
        await using var scope = services.CreateAsyncScope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>()
            .CreateAsync("cancelled", new MemoryStream(new ArchiveFixture().Zip()), cancellation.Token).AsTask());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("cancelled"));
    }

    [Fact]
    public async Task Missing_schema_is_observed_and_refused_without_creating_tables()
    {
        await using var database = new ArchiveDatabase();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var capabilities = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync();
        Assert.True(capabilities.DurableCapture);
        Assert.False(capabilities.SchemaReady);
        await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>()
            .CreateAsync("missing-schema", new MemoryStream(new ArchiveFixture().Zip())).AsTask());
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("Manifest", "elsa_release_archives")]
    [InlineData("Content", "elsa_release_archive_entries")]
    [InlineData("ManifestDigest", "elsa_release_archives")]
    [InlineData("ManifestByteLength", "elsa_release_archives")]
    [InlineData("ContentDigest", "elsa_release_archive_entries")]
    [InlineData("ContentByteLength", "elsa_release_archive_entries")]
    public async Task Partial_schema_with_lookup_and_version_columns_is_not_reported_ready(string column, string table)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            // Test-owned, fixed theory identifiers; no request or archive data enters this statement.
            command.CommandText = $"ALTER TABLE {table} RENAME COLUMN {column} TO RemovedRequiredColumn";
            await command.ExecuteNonQueryAsync();
        }
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var observed = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync();
        Assert.False(observed.SchemaReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_length_attachment_or_whitespace_only_manifest_tampering_refuses_exact_byte_integrity(bool manifest)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var fixture = new ArchiveFixture();
        await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("raw-integrity", new MemoryStream(fixture.Zip(alternateFormat: true)));
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var original = (await reader.FindAsync("raw-integrity"))!;
        var context = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
        if (manifest)
        {
            var changed = original.Transport.GetManifestBytes();
            var whitespace = Array.IndexOf(changed, (byte)'\n');
            Assert.True(whitespace >= 0);
            changed[whitespace] = (byte)' ';
            // Both manifests still describe the same canonical release and have the same raw length.
            var blobs = original.Transport.Members.Select(x => x.PayloadDigest).Distinct(StringComparer.Ordinal)
                .ToDictionary(x => x, x => (ReadOnlyMemory<byte>)original.Transport.GetBlobBytes(x), StringComparer.Ordinal);
            Assert.Equal(original.Transport.Digest, scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>().Validate(changed, blobs).Digest);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archives SET Manifest={changed}");
        }
        else
        {
            var changed = original.Transport.GetAttachmentBytes("attachments/receipt.bin"); changed[0] ^= 1;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archive_entries SET Content={changed} WHERE EntryKind=2");
        }
        Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(
            () => reader.FindAsync("raw-integrity").AsTask())).Code);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("entry")]
    [InlineData("expanded")]
    public async Task Actual_sqlite_lengths_and_total_budget_refuse_before_materializing_stored_payloads(string oversized)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var fixture = new ArchiveFixture();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("bounded",
                new MemoryStream(fixture.Zip(attachment: oversized == "expanded" ? new byte[1024] : null)));
            var context = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
            var bytes = new byte[8192];
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (oversized == "manifest")
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archives SET Manifest=zeroblob(8192), ManifestByteLength={8192L}, ManifestDigest={hash}");
            if (oversized == "entry")
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archive_entries SET Content=zeroblob(8192), ContentByteLength={8192L}, ContentDigest={hash} WHERE EntryKind=2");
        }
        var sql = new CaptureSelects();
        var limits = new ReleaseBundleLimits { MaxManifestBytes = 1024, MaxBlobBytes = 1024,
            MaxExpandedBytes = oversized == "expanded" ? fixture.ManifestBytes.Length + fixture.Payload.Length + 1024 - 1 : 16 * 1024 };
        await using var boundedServices = database.Services(interceptor: sql, limits: limits);
        await using var boundedScope = boundedServices.CreateAsyncScope();
        Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() =>
            boundedScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("bounded").AsTask())).Code);
        Assert.NotEmpty(sql.Commands);
        // Only scalar preflight/count or length(BLOB) metadata projections ran, never a binary payload projection.
        Assert.All(sql.Commands, projection => Assert.DoesNotMatch(@"(?<!length\()""[^""]+""\.""(?:Manifest|Content)""", projection));
    }

    [Theory]
    [InlineData("Name", false)]
    [InlineData("ManifestDigest", false)]
    [InlineData("CreatedAt", false)]
    [InlineData("Authority", false)]
    [InlineData("Name", true)]
    [InlineData("ManifestDigest", true)]
    public async Task Oversized_or_nul_hidden_metadata_is_refused_before_returning_unbounded_strings(string column, bool hiddenAfterNul)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("metadata", new MemoryStream(new ArchiveFixture().Zip()));
            await using var connection = new SqliteConnection(database.ConnectionString); await connection.OpenAsync();
            await using var update = connection.CreateCommand();
            // Fixed theory column names only; values stay parameterized.
            update.CommandText = $"UPDATE {(column == "Name" ? "elsa_release_archive_entries" : "elsa_release_archives")} SET {column}=CAST($value AS TEXT)";
            // Binary binding plus CAST preserves the entire NUL-bearing SQLite TEXT value; string parameter
            // binding can truncate at NUL and would fail to create the intended oversized stored representation.
            update.Parameters.AddWithValue("$value", System.Text.Encoding.UTF8.GetBytes((hiddenAfterNul ? new string('A', 64) + "\0" : "") + new string('A', 8192)));
            await update.ExecuteNonQueryAsync();
        }
        var sql = new CaptureSelects(column);
        await using var readServices = database.Services(interceptor: sql, limits: new ReleaseBundleLimits { MaxExpandedBytes = 4096 });
        await using var readScope = readServices.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("metadata").AsTask());
        Assert.Equal(column == "Authority" ? "archive_scope_identity_mismatch" : "archive_stored_content_invalid", error.Code);
        Assert.False(sql.ProtectedRowsReturned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_stored_in_blob_column_is_refused_without_fetching_payload(bool manifest)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("storage-class", new MemoryStream(new ArchiveFixture().Zip()));
            var context = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
            var text = string.Concat(Enumerable.Repeat("😀", 8));
            if (manifest) await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archives SET Manifest={text}, ManifestByteLength={8L}");
            else await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archive_entries SET Content={text}, ContentByteLength={8L} WHERE EntryKind=2");
        }
        var sql = new CaptureSelects(manifest ? "Manifest" : "Content");
        await using var readServices = database.Services(interceptor: sql);
        await using var readScope = readServices.CreateAsyncScope();
        Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() =>
            readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("storage-class").AsTask())).Code);
        Assert.False(sql.ProtectedRowsReturned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row_growth_after_read_snapshot_preserves_original_bounded_capture_then_next_read_refuses(bool manifest)
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("changed-after-preflight", new MemoryStream(new ArchiveFixture().Zip()));
        var change = new GrowBeforePayload(database.ConnectionString, manifest);
        await using var readServices = database.Services(interceptor: change,
            limits: new ReleaseBundleLimits { MaxManifestBytes = 1024, MaxBlobBytes = 1024 });
        await using var readScope = readServices.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var capture = await reader.FindAsync("changed-after-preflight");
        Assert.NotNull(capture);
        Assert.Equal(new ArchiveFixture().ManifestBytes, capture.Transport.GetManifestBytes());
        Assert.Equal(new ArchiveFixture().Payload, capture.Transport.GetBlobBytes(new ArchiveFixture().BlobDigest));
        Assert.True(change.Changed);
        Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() =>
            reader.FindAsync("changed-after-preflight").AsTask())).Code);
    }

    [Fact]
    public async Task Names_growing_between_sum_and_metadata_fetch_cannot_escape_consistent_read_budget()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using (var services = database.Services())
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("metadata-snapshot", new MemoryStream(new ArchiveFixture().Zip()));
        var change = new GrowBeforeMetadata(database.ConnectionString);
        await using var readServices = database.Services(interceptor: change, limits: new ReleaseBundleLimits { MaxExpandedBytes = 4096 });
        await using var readScope = readServices.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        var capture = await reader.FindAsync("metadata-snapshot");
        Assert.NotNull(capture);
        Assert.Equal(new ArchiveFixture().Attachment, capture.Transport.GetAttachmentBytes("attachments/receipt.bin"));
        Assert.True(change.Changed);
        Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => reader.FindAsync("metadata-snapshot").AsTask())).Code);
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=;Pooling=False")]
    [InlineData("Data Source=named-archive;Mode=Memory;Cache=Shared")]
    [InlineData("Data Source=file::memory:?cache=shared")]
    [InlineData("Data Source=file:named-archive?mode=memory&cache=shared")]
    public async Task Memory_and_temporary_sqlite_are_not_reported_as_durable_capture(string connectionString)
    {
        await using var services = ArchiveFixture.Services(connectionString);
        await using var scope = services.CreateAsyncScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync()).DurableCapture);
    }

    [Fact]
    public async Task Shared_memory_can_have_ready_schema_but_cannot_supply_durable_capture()
    {
        var connectionString = "Data Source=archive-" + Guid.NewGuid().ToString("N") + ";Mode=Memory;Cache=Shared;Pooling=False";
        await using var anchor = new SqliteConnection(connectionString); await anchor.OpenAsync();
        await using var services = ArchiveFixture.Services(connectionString);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ConsumerContext>().Database.EnsureCreatedAsync();
        var observed = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync();
        Assert.True(observed.SchemaReady);
        Assert.False(observed.DurableCapture);
        Assert.Equal("archive_durable_store_required", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() =>
            scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("ephemeral", new MemoryStream(new ArchiveFixture().Zip())).AsTask())).Code);
        await using var disposableServices = ArchiveFixture.Services(connectionString, requireDurable: false);
        await using var disposableScope = disposableServices.CreateAsyncScope();
        Assert.Equal(ReleaseArchiveCreateOutcome.Created, (await disposableScope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>()
            .CreateAsync("ephemeral", new MemoryStream(new ArchiveFixture().Zip()))).Outcome);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("ephemeral"));
    }

    [Fact]
    public async Task Stored_scope_corruption_and_missing_or_changed_parts_fail_closed_and_content_is_tracked_write_once()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        await using var services = database.Services();
        await using var scope = services.CreateAsyncScope();
        var fixture = new ArchiveFixture();
        var writer = scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>();
        foreach (var id in new[] { "scope", "missing", "bytes", "schema", "tracked" })
            await writer.CreateAsync(id, new MemoryStream(fixture.Zip()));
        var context = scope.ServiceProvider.GetRequiredService<ConsumerContext>();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archives SET Authority={EfRelationalIdentity.Encode("wrong")} WHERE CaptureId={EfRelationalIdentity.Encode("scope")}");
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM elsa_release_archive_entries WHERE CaptureId={EfRelationalIdentity.Encode("missing")}");
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archive_entries SET Content={new byte[] { 1, 2 }} WHERE CaptureId={EfRelationalIdentity.Encode("bytes")} AND EntryKind=1");
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_release_archives SET SchemaVersion={"future"} WHERE CaptureId={EfRelationalIdentity.Encode("schema")}");
        var reader = scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>();
        Assert.Equal("archive_scope_identity_mismatch", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => reader.FindAsync("scope").AsTask())).Code);
        foreach (var id in new[] { "missing", "bytes", "schema" })
            Assert.Equal("archive_stored_content_invalid", (await Assert.ThrowsAsync<ReleaseArchiveStoreException>(() => reader.FindAsync(id).AsTask())).Code);
        var tracked = await context.Set<ReleaseArchiveRow>().SingleAsync(x => x.CaptureId == EfRelationalIdentity.Encode("tracked"));
        tracked.Manifest = [0];
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Unqualified_providers_are_explicitly_refused()
    {
        Assert.Throws<NotSupportedException>(() => new ModelBuilder().AddReleaseArchiveModel(EfProviderNames.PostgreSql));
    }

    [Fact]
    public async Task Killed_writer_has_no_visible_uncommitted_header_and_new_process_reopens_original_bytes()
    {
        await using var database = await ArchiveDatabase.CreateAsync();
        var signal = database.Path + ".signal";
        using var child = StartProbe("interrupt", database.Path, signal);
        var errors = child.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(signal))
            {
                Assert.False(child.HasExited, "The interruption probe exited before its uncommitted-header signal.");
                await Task.Delay(20, timeout.Token);
            }
            await using (var services = database.Services())
            await using (var scope = services.CreateAsyncScope())
                Assert.Null(await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>()
                    .FindAsync("process-capture").AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);
            Assert.Equal("missing", (await RunProbe("read", database.Path, signal)).Trim());
            await using (var services = database.Services())
            await using (var scope = services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("process-capture", new MemoryStream(new ArchiveFixture().Zip()));
            Assert.Equal("reopened-original-bytes", (await RunProbe("read", database.Path, signal)).Trim());
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            await errors;
            File.Delete(signal);
        }
    }

    private static Process StartProbe(string operation, string path, string signal)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "Elsa.Server.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = typeof(ReleaseArchiveStoreTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var assembly = System.IO.Path.Combine(root!.FullName,
            "Essentials/tests/Workflows/Publishing/Releases/Persistence/EntityFrameworkCore/RestartProbe/bin", configuration,
            "net10.0/Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.RestartProbe.dll");
        Assert.True(File.Exists(assembly), "The process probe must be built by the test project's build-only reference.");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(assembly); start.ArgumentList.Add(operation); start.ArgumentList.Add(path); start.ArgumentList.Add(signal);
        return Process.Start(start)!;
    }

    private static async Task<string> RunProbe(string operation, string path, string signal)
    {
        using var process = StartProbe(operation, path, signal);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private sealed class FailAfterHeader : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            Armed ? throw new IOException("Injected interruption after the header insert.") : ValueTask.FromResult(result);
    }

    private sealed class LoseCommitAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException("Injected lost commit acknowledgement.");
    }

    private sealed class CancelAfterHeader(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CaptureSelects(string? protectedColumn = null) : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];
        internal bool ProtectedRowsReturned { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText.Split("FROM", 2, StringSplitOptions.None)[0]);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var projection = command.CommandText.Split("FROM", 2, StringSplitOptions.None)[0];
            if (protectedColumn is not null && Regex.IsMatch(projection, @"(?<!length\()""[^""]+""\.""" + protectedColumn + @""""))
                ProtectedRowsReturned |= result.HasRows;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class GrowBeforePayload(string connectionString, bool manifest) : DbCommandInterceptor
    {
        internal bool Changed { get; private set; }
        internal bool PayloadReturnedRows { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var projection = command.CommandText.Split("FROM", 2, StringSplitOptions.None)[0];
            if (Changed || !projection.Contains(manifest ? "\"Manifest\"" : "\"Content\"", StringComparison.Ordinal) ||
                projection.Contains("length(", StringComparison.OrdinalIgnoreCase)) return result;
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var update = connection.CreateCommand();
            update.CommandText = manifest
                ? "UPDATE elsa_release_archives SET Manifest=zeroblob(8192)"
                : "UPDATE elsa_release_archive_entries SET Content=zeroblob(8192)";
            await update.ExecuteNonQueryAsync(cancellationToken);
            Changed = true;
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var projection = command.CommandText.Split("FROM", 2, StringSplitOptions.None)[0];
            if (Changed && projection.Contains(manifest ? "\"Manifest\"" : "\"Content\"", StringComparison.Ordinal) &&
                !projection.Contains("length(", StringComparison.OrdinalIgnoreCase)) PayloadReturnedRows |= result.HasRows;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class GrowBeforeMetadata(string connectionString) : DbCommandInterceptor
    {
        internal bool Changed { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var projection = command.CommandText.Split("FROM", 2, StringSplitOptions.None)[0];
            if (Changed || !Regex.IsMatch(projection, @"(?<!length\()""[^""]+""\.""Name""")) return result;
            await using var connection = new SqliteConnection(connectionString); await connection.OpenAsync(cancellationToken);
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE elsa_release_archive_entries SET Name=$name";
            update.Parameters.AddWithValue("$name", new string('A', 1000));
            await update.ExecuteNonQueryAsync(cancellationToken);
            Changed = true;
            return result;
        }
    }

    private sealed class ArchiveDatabase : IAsyncDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "release-archive-" + Guid.NewGuid().ToString("N") + ".db");
        internal string ConnectionString => "Data Source=" + Path + ";Pooling=False;Default Timeout=5";
        internal ServiceProvider Services(SourceScope? scope = null, MutableAccess? access = null, IInterceptor? interceptor = null, ReleaseBundleLimits? limits = null) =>
            ArchiveFixture.Services(ConnectionString, scope, access, interceptor, limits);
        internal static async Task<ArchiveDatabase> CreateAsync()
        {
            var database = new ArchiveDatabase();
            await using var services = database.Services();
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ConsumerContext>().Database.EnsureCreatedAsync();
            return database;
        }
        public ValueTask DisposeAsync()
        {
            File.Delete(Path); File.Delete(Path + "-wal"); File.Delete(Path + "-shm");
            return ValueTask.CompletedTask;
        }
    }
}
