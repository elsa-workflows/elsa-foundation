using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Unregistered physical mechanics with an explicitly TEST-ONLY canonical family codec, not Message acceptance.</summary>
public sealed class RuntimeDeliverySourceReadClaimTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("store");
    private const string Family = RuntimeIngressCanonicalFamilyReader.BusinessMessage;

    [Fact]
    public async Task Read_decodes_the_actual_canonical_header_and_checks_exact_opaque_address_and_context()
    {
        await using var fixture = await Fixture.CreateAsync(Production(authority: new("system-\ud800", "root\0id")));
        await fixture.AddAsync("work-\udc00");
        var source = await fixture.Store().ReadAsync(Address("work-\udc00"));
        Assert.NotNull(source); Assert.Equal(Address("work-\udc00"), source.Address); Assert.Equal(1, source.Revision);
        Assert.Equal("system-\ud800", source.Authority.SystemIdentity); Assert.Equal("root\0id", source.Authority.RootInitiator);
        Assert.Null(await fixture.Store().ReadAsync(Address("absent")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().ReadAsync(new(Family, new("other"), "work-\udc00")).AsTask());
    }

    [Fact]
    public async Task Full_SQL_context_filters_before_Take_despite_real_legacy_authority_aliases_and_coarse_collision()
    {
        var wanted = Production(authority: new("system-\ud800", "root"));
        var foreign = Production(authority: new("system-\ud801", "root"));
        Assert.Equal(wanted.AuthorityPartitionKey, foreign.AuthorityPartitionKey);
        await using var fixture = await Fixture.CreateAsync(wanted);
        await fixture.AddAsync("foreign", foreign, order: -20); await fixture.AddAsync("rightful", wanted, order: -10);
        var foreignRow = await fixture.WorkAsync("foreign");
        foreignRow.FullDiscoveryIdentityHash = RuntimeDeliveryDiscoveryProjection.Create(wanted).Hash;
        await fixture.ReplaceAsync(foreignRow);
        var claim = Assert.Single(await fixture.Store().ClaimAsync(Request(wanted, limit: 1)));
        Assert.Equal("rightful", claim.WorkId); Assert.Equal(1, claim.SourceRevision);
        Assert.Equal(0, (await fixture.WorkAsync("foreign")).FencingToken);
    }

    [Theory]
    [InlineData("tenant")] [InlineData("execution-partition")] [InlineData("run-kind")] [InlineData("authority-metadata")]
    public async Task Other_exact_contexts_cannot_clip_a_bounded_canonical_source_join(string changed)
    {
        var wanted = Production();
        var foreign = new RuntimeDeliveryDiscoveryPartition(Scope, changed == "tenant" ? "tenant" : null,
            changed == "run-kind" ? WorkflowRunKind.BackgroundWeaverRun : WorkflowRunKind.PublishedRun,
            new("system", "root", changed == "authority-metadata" ? new Dictionary<string, string> { ["key"] = "value" } : null),
            changed == "execution-partition" ? new("execution") : null);
        await using var fixture = await Fixture.CreateAsync(wanted);
        await fixture.AddAsync("foreign", foreign, order: -20); await fixture.AddAsync("rightful", wanted, order: -10);
        Assert.Equal("rightful", Assert.Single(await fixture.Store().ClaimAsync(Request(wanted, limit: 1))).WorkId);
        Assert.Equal(0, (await fixture.WorkAsync("foreign")).FencingToken);
    }

    [Fact]
    public async Task Bounded_claim_is_deterministic_exclusive_and_fences_mutable_source_separately_from_immutable_header()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAsync("later", order: -10); await fixture.AddAsync("earlier", order: -20);
        var first = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition, limit: 1)));
        Assert.Equal("earlier", first.WorkId); Assert.Equal(2, first.Revision); Assert.Equal(1, first.FencingToken);
        var second = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition, owner: "other", limit: 1)));
        Assert.Equal("later", second.WorkId);
        Assert.Empty(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        Assert.Equal(2, (await fixture.SourceAsync("earlier")).Revision);
        Assert.Equal(1, (await fixture.Store().ReadAsync(Address("earlier")))!.Revision);
        Assert.Equal("worker", EfRelationalIdentity.Decode((await fixture.WorkAsync("earlier")).OwnerId!));
    }

    [Theory]
    [InlineData("unknown-family")] [InlineData("unknown-kind")] [InlineData("unspecified")]
    public async Task Unrecognized_or_unspecified_request_refuses_without_claiming_supported_work(string defect)
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        var request = defect switch
        {
            "unknown-family" => new RuntimeDeliveryClaimRequest(Scope, [Family, "StimulusIngress"], ["dispatch"], "worker", TimeSpan.FromSeconds(10), 1, fixture.Owner.Partition),
            "unknown-kind" => new RuntimeDeliveryClaimRequest(Scope, [Family], ["dispatch", "unknown"], "worker", TimeSpan.FromSeconds(10), 1, fixture.Owner.Partition),
            _ => new RuntimeDeliveryClaimRequest(Scope, [Family], ["dispatch"], "worker", TimeSpan.FromSeconds(10))
        };
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Store().ClaimAsync(request).AsTask());
        Assert.Equal(1, (await fixture.WorkAsync("work")).Revision);
    }

    [Theory]
    [InlineData("missing-source")] [InlineData("header-version")] [InlineData("participant-version")]
    [InlineData("header-binding")] [InlineData("source-status")] [InlineData("role")]
    public async Task A_matching_owed_obligation_with_no_exact_source_capability_refuses_before_any_page_mutation(string defect)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAsync("blocked", order: -20); await fixture.AddAsync("otherwise-valid", order: -10);
        if (defect == "missing-source")
            await fixture.Context.RuntimeIngressSources.Where(row => row.Id == SourceId("blocked")).ExecuteDeleteAsync();
        else if (defect is "header-version" or "source-status")
        {
            var source = await fixture.SourceAsync("blocked");
            if (defect == "header-version") source.HeaderVersion = 2; else source.Status = 2;
            fixture.Context.Update(source); await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        }
        else
        {
            var work = await fixture.WorkAsync("blocked");
            if (defect == "participant-version") work.RequiredParticipantVersion = 2;
            else if (defect == "role") work.Role = (int)RuntimeDeliveryWorkRole.Plan;
            else work.SourceHeaderRevision = 2;
            await fixture.ReplaceAsync(work);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition, limit: 1)).AsTask());
        Assert.Equal(1, (await fixture.WorkAsync("otherwise-valid")).Revision);
        Assert.Equal(1, (await fixture.SourceAsync("otherwise-valid")).Revision);
    }

    [Fact]
    public async Task Renewal_extends_the_current_lease_and_invalidates_the_previous_revision_without_shortening()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        var original = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        fixture.Clock.Value = Now.AddTicks(-1);
        Assert.Null(await fixture.Store().RenewAsync(original, TimeSpan.FromSeconds(30)));
        fixture.Clock.Value = Now.AddSeconds(2);
        var renewed = await fixture.Store().RenewAsync(original, TimeSpan.FromSeconds(30));
        Assert.NotNull(renewed); Assert.Equal(Now.AddSeconds(32), renewed.VisibleAfter);
        Assert.Equal(original.FencingToken, renewed.FencingToken); Assert.Equal(original.Revision + 1, renewed.Revision);
        Assert.Null(await fixture.Store().RenewAsync(original, TimeSpan.FromSeconds(40)));
        var noShortening = await fixture.Store().RenewAsync(renewed, TimeSpan.FromSeconds(1));
        Assert.NotNull(noShortening); Assert.Equal(renewed.VisibleAfter, noShortening.VisibleAfter);
    }

    [Fact]
    public async Task Exact_expiry_requires_a_new_fence_and_old_owner_or_payload_cannot_renew_the_successor()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        var original = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        fixture.Clock.Value = original.VisibleAfter;
        Assert.Null(await fixture.Store().RenewAsync(original, TimeSpan.FromMinutes(1)));
        var successor = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition, owner: "successor")));
        Assert.Equal(original.FencingToken + 1, successor.FencingToken);
        Assert.Null(await fixture.Store().RenewAsync(original, TimeSpan.FromMinutes(1)));
        var forged = new RuntimeDeliveryWorkClaim(successor.Source, successor.SourceRevision, successor.HeaderFingerprint,
            successor.WorkId, successor.WorkKind, successor.Role, JsonSerializer.SerializeToElement(new { forged = true }),
            successor.ContentFingerprint, successor.Revision, successor.OwnerId, successor.FencingToken, successor.ClaimedAt, successor.VisibleAfter);
        Assert.Null(await fixture.Store().RenewAsync(forged, TimeSpan.FromMinutes(1)));
        Assert.Equal(successor.Revision, (await fixture.WorkAsync("work")).Revision);
    }

    [Fact]
    public async Task A_valid_but_terminal_canonical_source_cannot_renew_an_earlier_transport_claim()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        var claim = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        var row = await fixture.SourceAsync("work"); var source = RuntimeArtifactJson.Deserialize<FixtureSource>(row.ContentJson);
        row.ContentJson = RuntimeArtifactJson.Serialize(source with { Status = 1 }); row.Status = 1; row.Revision++;
        fixture.Context.Update(row); await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        Assert.NotNull(await fixture.Store().ReadAsync(Address("work"))); // Metadata remains readable.
        Assert.Null(await fixture.Store().RenewAsync(claim, TimeSpan.FromMinutes(1)));
        Assert.Equal(row.Revision, (await fixture.SourceAsync("work")).Revision);
        Assert.Equal(claim.Revision, (await fixture.WorkAsync("work")).Revision);
    }

    [Fact]
    public async Task A_clock_advance_after_actual_renewal_SQL_rolls_back_an_elapsed_lease()
    {
        var delay = new AfterWorkWrite(); await using var fixture = await Fixture.CreateAsync(interceptor: delay); await fixture.AddAsync("work");
        var claim = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        delay.Change = () => fixture.Clock.Value = Now.AddMinutes(2);
        Assert.Null(await fixture.Store().RenewAsync(claim, TimeSpan.FromSeconds(30)));
        Assert.True(delay.Changed); Assert.Equal(claim.Revision, (await fixture.WorkAsync("work")).Revision);
        Assert.Equal(2, (await fixture.SourceAsync("work")).Revision);
    }

    [Fact]
    public async Task Authority_changed_during_the_last_test_ledger_await_cannot_receive_the_old_header()
    {
        var change = new AfterLedgerRead(); var execution = new WorkflowExecutionPartition("execution");
        var test = new WorkflowTestScope("test", Now.AddHours(1), null, execution);
        var partition = new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.TestRun, new("system", "root"), execution, test);
        await using var fixture = await Fixture.CreateAsync(partition, change); await fixture.AddAsync("work");
        fixture.Context.WorkflowTestScopes.Add(WorkflowTestScopeEfSupport.ToEntity(new(test, WorkflowTestScopeState.Open, Now.AddHours(-1)), Scope.Value,
            WorkflowTestScopeEfSupport.Id(Scope.Value, test.ScopeId)));
        await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        change.Change = () => fixture.Owner.Partition = new(Scope, null, WorkflowRunKind.TestRun, new("other", "root"), execution, test);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().ReadAsync(Address("work")).AsTask());
        Assert.True(change.Changed);
    }

    [Fact]
    public async Task Canonical_payload_corruption_rolls_back_the_entire_bounded_claim_batch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAsync("first", order: -20); await fixture.AddAsync("corrupt", order: -10);
        var corrupt = await fixture.WorkAsync("corrupt"); corrupt.ContentFingerprint = "wrong-fingerprint"; await fixture.ReplaceAsync(corrupt);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition, limit: 2)).AsTask());
        Assert.Equal(1, (await fixture.WorkAsync("first")).Revision); Assert.Equal(1, (await fixture.SourceAsync("first")).Revision);
        Assert.Equal(0, (await fixture.WorkAsync("first")).FencingToken);
    }

    [Fact]
    public async Task A_failure_after_actual_source_and_work_SQL_updates_rolls_back_both_rows()
    {
        var failure = new FailAfterWorkWrite();
        await using var fixture = await Fixture.CreateAsync(interceptor: failure); await fixture.AddAsync("work");
        await Assert.ThrowsAsync<IOException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)).AsTask());
        Assert.True(failure.FailedAfterActualUpdate);
        Assert.Equal(1, (await fixture.SourceAsync("work")).Revision);
        Assert.Equal(1, (await fixture.WorkAsync("work")).Revision); Assert.Null((await fixture.WorkAsync("work")).OwnerId);
    }

    [Fact]
    public async Task An_injected_late_revision_change_loses_the_real_SQL_CAS_and_rolls_back_the_source_touch()
    {
        var race = new ChangeRevisionBeforeWorkWrite();
        await using var fixture = await Fixture.CreateAsync(interceptor: race); await fixture.AddAsync("work");
        Assert.Empty(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        Assert.True(race.Injected); Assert.Equal(1, (await fixture.SourceAsync("work")).Revision);
        Assert.Equal(1, (await fixture.WorkAsync("work")).Revision);
    }

    [Theory]
    [InlineData("revision")] [InlineData("fence")]
    public async Task Counter_overflow_refuses_and_rolls_back_prior_canonical_source_touch(string counter)
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work"); var row = await fixture.WorkAsync("work");
        if (counter == "revision") row.Revision = long.MaxValue;
        else
        {
            row.Status = RuntimeDeliveryWorkTransport.Claimed; row.FencingToken = long.MaxValue;
            row.OwnerId = EfRelationalIdentity.Encode("old"); row.ClaimedAtUtcTicks = Now.AddSeconds(-20).UtcTicks;
            row.VisibleAfterUtcTicks = row.ClaimableAtUtcTicks = Now.AddSeconds(-10).UtcTicks;
        }
        await fixture.ReplaceAsync(row);
        await Assert.ThrowsAsync<OverflowException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)).AsTask());
        Assert.Equal(1, (await fixture.SourceAsync("work")).Revision);
        Assert.Equal(counter == "revision" ? long.MaxValue : 1, (await fixture.WorkAsync("work")).Revision);
    }

    [Theory]
    [InlineData("closed")] [InlineData("expiry")] [InlineData("partition")]
    public async Task Full_finite_test_context_and_current_ledger_are_checked_in_the_owned_boundary(string defect)
    {
        var execution = new WorkflowExecutionPartition("execution");
        var test = new WorkflowTestScope("test", Now.AddHours(1), null, execution);
        var partition = new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.TestRun, new("system", "root"), execution, test);
        await using var fixture = await Fixture.CreateAsync(partition); await fixture.AddAsync("work");
        var retained = defect == "expiry" ? new WorkflowTestScope("test", Now.AddHours(2), null, execution)
            : defect == "partition" ? new WorkflowTestScope("test", test.ExpiresAt, null, new("other")) : test;
        var record = defect == "closed" ? new WorkflowTestScopeRecord(retained, WorkflowTestScopeState.Closed, Now.AddHours(-1),
            Now.AddMinutes(-2), Now.AddMinutes(-1), WorkflowTestScopeCloseReason.ExplicitTeardown)
            : new WorkflowTestScopeRecord(retained, WorkflowTestScopeState.Open, Now.AddHours(-1));
        fixture.Context.WorkflowTestScopes.Add(WorkflowTestScopeEfSupport.ToEntity(record, Scope.Value, WorkflowTestScopeEfSupport.Id(Scope.Value, test.ScopeId)));
        await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<TestScopeAdmissionException>(() => fixture.Store().ClaimAsync(Request(partition)).AsTask());
        Assert.Equal(1, (await fixture.WorkAsync("work")).Revision);
    }

    [Fact]
    public async Task Open_test_ledger_touch_commits_with_claim_and_exact_expiry_then_refuses_renewal()
    {
        var execution = new WorkflowExecutionPartition("execution"); var test = new WorkflowTestScope("test", Now.AddHours(1), null, execution);
        var partition = new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.TestRun, new("system", "root"), execution, test);
        await using var fixture = await Fixture.CreateAsync(partition); await fixture.AddAsync("work");
        fixture.Context.WorkflowTestScopes.Add(WorkflowTestScopeEfSupport.ToEntity(new(test, WorkflowTestScopeState.Open, Now.AddHours(-1)), Scope.Value,
            WorkflowTestScopeEfSupport.Id(Scope.Value, test.ScopeId)));
        await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        var claim = Assert.Single(await fixture.Store().ClaimAsync(Request(partition)));
        Assert.Equal(1, (await fixture.Context.WorkflowTestScopes.AsNoTracking().SingleAsync()).Revision);
        fixture.Clock.Value = test.ExpiresAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().RenewAsync(claim, TimeSpan.FromSeconds(10)).AsTask());
    }

    [Fact]
    public async Task Changed_current_authority_cannot_renew_a_retained_claim_or_read_its_payload()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        var claim = Assert.Single(await fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)));
        fixture.Owner.Partition = Production(authority: new("other", "root"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().RenewAsync(claim, TimeSpan.FromSeconds(10)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().ReadAsync(Address("work")).AsTask());
        Assert.Equal(claim.Revision, (await fixture.WorkAsync("work")).Revision);
    }

    [Fact]
    public async Task Caller_transaction_or_unsaved_changes_are_refused_and_complete_source_participation_stays_disabled()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.AddAsync("work");
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)).AsTask());
        var tracked = await fixture.Context.RuntimeDeliveryWorks.SingleAsync(); tracked.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store().ClaimAsync(Request(fixture.Owner.Partition)).AsTask());
        fixture.Context.ChangeTracker.Clear();
        IRuntimeDeliverySourceStore store = fixture.Store(); Assert.False(store.CanParticipate(null!));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.CommitAsync(null!).AsTask());
        Assert.Equal(1, (await fixture.WorkAsync("work")).Revision);
    }

    private static RuntimeDeliveryDiscoveryPartition Production(WorkflowExecutionAuthoritySnapshot? authority = null) =>
        new(Scope, null, WorkflowRunKind.PublishedRun, authority ?? new("system", "root"));
    private static RuntimeDeliverySourceAddress Address(string id) => new(Family, Scope, id);
    private static string SourceId(string id) => EfRelationalIdentity.HashLengthFramed(Scope.Value, Family, id);
    private static RuntimeDeliveryClaimRequest Request(RuntimeDeliveryDiscoveryPartition partition, string owner = "worker", int limit = 10) =>
        new(partition.Scope, [Family], ["dispatch"], owner, TimeSpan.FromSeconds(10), limit, partition);

    private sealed class Clock : TimeProvider { internal DateTimeOffset Value = Now; public override DateTimeOffset GetUtcNow() => Value; }
    private sealed class Owner(RuntimeDeliveryDiscoveryPartition partition) : IRuntimeDeliveryDiscoveryOwner
    { internal RuntimeDeliveryDiscoveryPartition Partition = partition; public RuntimeDeliveryDiscoveryPartition CaptureCurrent() => Partition; }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }

    private sealed class Fixture(SqliteConnection connection, RuntimeSqliteDbContext context, RuntimeDeliveryDiscoveryPartition partition) : IAsyncDisposable
    {
        internal RuntimeSqliteDbContext Context { get; } = context;
        internal Clock Clock { get; } = new();
        internal Owner Owner { get; } = new(partition);
        internal static async Task<Fixture> CreateAsync(RuntimeDeliveryDiscoveryPartition? partition = null, DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var context = new RuntimeSqliteDbContext(builder.Options); await context.Database.EnsureCreatedAsync();
            return new(connection, context, partition ?? Production());
        }
        internal EfRuntimeDeliverySourceStore Store() => new(Context, new Access(), Owner, [new RuntimeIngressCanonicalFamilyReader(new FixtureCodec())], Clock);
        internal async Task AddAsync(string id, RuntimeDeliveryDiscoveryPartition? partition = null, int order = -1)
        {
            partition ??= Owner.Partition; var address = Address(id); var projection = RuntimeDeliveryDiscoveryProjection.Create(partition);
            var header = new FixtureHeader(address, partition, 1, JsonSerializer.SerializeToElement(new { accepted = true }));
            var source = new RuntimeIngressSourceEntity
            {
                Id = SourceId(id), ScopeKey = EfRelationalIdentity.Encode(Scope.Value), ScopeKeyHash = EfRelationalIdentity.Hash(Scope.Value),
                SourceFamily = EfRelationalIdentity.Encode(Family), SourceFamilyHash = EfRelationalIdentity.Hash(Family),
                SourceId = EfRelationalIdentity.Encode(id), SourceIdHash = EfRelationalIdentity.Hash(id),
                TenantId = partition.TenantId is null ? null : EfRelationalIdentity.Encode(partition.TenantId),
                TenantIdHash = partition.TenantId is null ? null : EfRelationalIdentity.Hash(partition.TenantId), RunKind = (int)partition.RunKind,
                TestScopeId = partition.TestScope is null ? null : EfRelationalIdentity.Encode(partition.TestScope.ScopeId),
                TestScopeIdHash = partition.TestScope is null ? null : EfRelationalIdentity.Hash(partition.TestScope.ScopeId),
                AuthorityPartitionKey = partition.AuthorityPartitionKey, FullDiscoveryIdentity = projection.Identity, FullDiscoveryIdentityHash = projection.Hash,
                HeaderVersion = 1, HeaderRevision = 1, RequiredParticipantVersion = 1, HeaderFingerprint = Fingerprint(header), Status = 0,
                RecordedAtUtcTicks = Now.UtcTicks, AcceptanceOperationId = "fixture-accept:" + id, AcceptanceOperationFingerprint = "fixture-accept:v1",
                OperationProofJson = "{}", ContentJson = RuntimeArtifactJson.Serialize(new FixtureSource(header, 0)), SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
            };
            var work = new FixtureWork(address, id, "dispatch", RuntimeDeliveryWorkRole.Dispatch, Now.AddSeconds(order), JsonSerializer.SerializeToElement(new { payload = 7 }));
            Context.RuntimeIngressSources.Add(source);
            Context.RuntimeDeliveryWorks.Add(new()
            {
                Id = EfRelationalIdentity.HashLengthFramed(Scope.Value, Family, id, id), ScopeKey = source.ScopeKey, ScopeKeyHash = source.ScopeKeyHash,
                SourceFamily = source.SourceFamily, SourceFamilyHash = source.SourceFamilyHash, SourceId = source.SourceId, SourceIdHash = source.SourceIdHash,
                TenantId = source.TenantId, TenantIdHash = source.TenantIdHash, RunKind = source.RunKind, TestScopeId = source.TestScopeId, TestScopeIdHash = source.TestScopeIdHash,
                AuthorityPartitionKey = source.AuthorityPartitionKey, FullDiscoveryIdentity = source.FullDiscoveryIdentity, FullDiscoveryIdentityHash = source.FullDiscoveryIdentityHash,
                SourceHeaderRevision = 1, SourceHeaderFingerprint = source.HeaderFingerprint, RequiredParticipantVersion = 1,
                WorkId = EfRelationalIdentity.Encode(id), WorkIdHash = EfRelationalIdentity.Hash(id), WorkKind = EfRelationalIdentity.Encode("dispatch"), WorkKindHash = EfRelationalIdentity.Hash("dispatch"),
                Role = (int)RuntimeDeliveryWorkRole.Dispatch, ContentFingerprint = Fingerprint(work), Status = RuntimeDeliveryWorkTransport.Pending,
                RecordedAtUtcTicks = work.NotBefore.UtcTicks, ClaimableIsEligible = true, ClaimableAtUtcTicks = work.NotBefore.UtcTicks,
                ContentJson = RuntimeArtifactJson.Serialize(work), SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
            });
            await Context.SaveChangesAsync(); Context.ChangeTracker.Clear();
        }
        internal Task<RuntimeDeliveryWorkEntity> WorkAsync(string id) => Context.RuntimeDeliveryWorks.AsNoTracking().SingleAsync(row => row.WorkId == EfRelationalIdentity.Encode(id));
        internal Task<RuntimeIngressSourceEntity> SourceAsync(string id) => Context.RuntimeIngressSources.AsNoTracking().SingleAsync(row => row.Id == SourceId(id));
        internal async Task ReplaceAsync(RuntimeDeliveryWorkEntity row) { Context.Update(row); await Context.SaveChangesAsync(); Context.ChangeTracker.Clear(); }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
    }

    // Fixture-only lossless wrapper/header/work format. No BusinessMessage production wire or business policy is adopted.
    private sealed record FixtureHeader(RuntimeDeliverySourceAddress Address, RuntimeDeliveryDiscoveryPartition Context, long HeaderRevision, JsonElement Data);
    private sealed record FixtureSource(FixtureHeader Header, int Status);
    private sealed record FixtureWork(RuntimeDeliverySourceAddress Source, string WorkId, string Kind, RuntimeDeliveryWorkRole Role, DateTimeOffset NotBefore, JsonElement Payload);
    private static string Fingerprint<T>(T value) => "fixture/v1:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(value))));
    private static JsonElement Element<T>(T value) { using var document = JsonDocument.Parse(RuntimeArtifactJson.Serialize(value)); return document.RootElement.Clone(); }
    private sealed class FixtureCodec : RuntimeIngressCanonicalCodec
    {
        internal override int HeaderVersion => 1;
        internal override int ParticipantVersion => 1;
        internal override IReadOnlyList<int> ClaimableSourceStatuses => [0];
        internal override IReadOnlyList<RuntimeDeliverySupportedWork> Work => [new("dispatch", RuntimeDeliveryWorkRole.Dispatch)];
        internal override RuntimeDeliveryCanonicalHeader ReadHeader(string contentJson)
        {
            var source = RuntimeArtifactJson.Deserialize<FixtureSource>(contentJson); var header = source.Header; var partition = header.Context;
            return new(new(header.Address, HeaderVersion, header.HeaderRevision, Fingerprint(header), Element(header), partition.Authority,
                partition.TenantId, partition.RunKind, partition.TestScope), partition, source.Status);
        }
        internal override RuntimeDeliveryCanonicalWork ReadWork(string contentJson)
        {
            var work = RuntimeArtifactJson.Deserialize<FixtureWork>(contentJson);
            return new(work.Source, work.WorkId, work.Kind, work.Role, Element(work), Fingerprint(work), work.NotBefore);
        }
        internal override void ValidateClaimable(string contentJson, RuntimeDeliveryCanonicalWork work)
        {
            var source = RuntimeArtifactJson.Deserialize<FixtureSource>(contentJson);
            if (source.Status != 0 || source.Header.Address != work.Source || source.Header.Data.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The fixture canonical source cannot own this work.");
        }
    }

    private sealed class FailAfterWorkWrite : DbCommandInterceptor
    {
        internal bool FailedAfterActualUpdate;
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"" + RuntimeDeliverySourceEfModule.WorkTableName + "\"", StringComparison.Ordinal))
            { FailedAfterActualUpdate = true; throw new IOException("fixture failure after actual work write"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class AfterWorkWrite : DbCommandInterceptor
    {
        internal Action? Change; internal bool Changed;
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Change is not null && command.CommandText.StartsWith("UPDATE \"" + RuntimeDeliverySourceEfModule.WorkTableName + "\"", StringComparison.Ordinal))
            { Change(); Changed = true; }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class AfterLedgerRead : DbCommandInterceptor
    {
        internal Action? Change; internal bool Changed;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Change is not null && command.CommandText.Contains("FROM \"" + RuntimeWorkflowTestScopeEfModule.TableName + "\"", StringComparison.Ordinal))
            { Change(); Changed = true; }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ChangeRevisionBeforeWorkWrite : DbCommandInterceptor
    {
        internal bool Injected;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Injected && command.CommandText.StartsWith("UPDATE \"" + RuntimeDeliverySourceEfModule.WorkTableName + "\"", StringComparison.Ordinal))
            {
                Injected = true; await using var change = command.Connection!.CreateCommand(); change.Transaction = command.Transaction;
                change.CommandText = "UPDATE \"" + RuntimeDeliverySourceEfModule.WorkTableName + "\" SET Revision = Revision + 1";
                await change.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
