using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;
using Elsa.Weaver.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Persistence.Tests;

/// <summary>Actual Weaver writes must fit the owning context's shared admission, including nested retained outcomes.</summary>
public sealed class WeaverSchemaWriteAdmissionTests
{
    private const string Id = "write-session";
    private const string Owner = "write-owner";

    public static TheoryData<string> NewRepresentations => new()
    {
        "prepared-classes", "performed-proposal", "proposal-result-code", "proposal-result-scope",
        "execution-status", "execution-classes", "execution-result", "execution-error", "message-error",
        "message-result-code", "message-result-scope", "message-history", "paused-results", "paused-history",
        "snapshot-code", "snapshot-scope", "empty-code"
    };

    [Theory]
    [MemberData(nameof(NewRepresentations))]
    public async Task Held_legacy_create_and_update_refuse_each_new_representation_without_changing_any_record(string path)
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        var store = fixture.Records;
        var write = NewWrite(path);
        var record = new WeaverSessionRecord(Records.Session(Id))
        {
            Messages = write.Messages, Proposals = write.Proposals, PausedTurn = write.PausedTurn, Snapshots = write.Snapshots
        };
        var empty = await fixture.Database.ReadAllTextAsync();
        var create = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.CreateAsync(record, Owner));
        Assert.Equal(("Weaver", "1.0.0", "1.1.0"), (create.Family, create.WriteVersion, create.RequiredVersion));
        Assert.Null(await store.LoadAsync(Id));
        Assert.Equal(empty, await fixture.Database.ReadAllTextAsync());

        Assert.True((await store.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        var before = WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id));
        var text = await fixture.Database.ReadAllTextAsync();
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.WriteAsync(write));
        Assert.Equal(before, WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id)));
        Assert.Equal(text, await fixture.Database.ReadAllTextAsync());
        await fixture.FinalizeAsync("1.1.0");
        Assert.True((await store.WriteAsync(write)).Succeeded);
        var loaded = (await store.LoadAsync(Id))!;
        Assert.Equal(WeaverInvocationIntegrity.Digest(write.Messages), WeaverInvocationIntegrity.Digest(loaded.Messages));
        Assert.Equal(WeaverInvocationIntegrity.Digest(write.PausedTurn), WeaverInvocationIntegrity.Digest(loaded.PausedTurn));
        Assert.Equal(WeaverInvocationIntegrity.Digest(write.Snapshots), WeaverInvocationIntegrity.Digest(loaded.Snapshots));
        Assert.Equal(WeaverInvocationIntegrity.Digest(write.Proposals.Select(x => x with { Version = 1 }).ToArray()), WeaverInvocationIntegrity.Digest(loaded.Proposals));
    }

    [Fact]
    public async Task Direct_ungated_consumers_keep_legacy_writes_and_cannot_write_new_behavior_or_downstamp_retained_content()
    {
        using var database = new WeaverSqliteDatabase();
        var store = database.RecordStore();
        Assert.True((await store.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.WriteAsync(NewWrite("performed-proposal")));
        Assert.Equal(1, await database.ExecuteAsync("UPDATE elsa_weaver_sessions SET SchemaVersion = '1.1.0'"));
        var before = WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id));
        var refusal = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.WriteAsync(new(Id, new(Owner, 1))
        { PendingAudits = [Audit("pending")] }));
        Assert.Equal(("1.0.0", "1.1.0"), (refusal.WriteVersion, refusal.RequiredVersion));
        Assert.Equal(before, WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id)));
    }

    [Fact]
    public async Task Finalization_changes_actual_session_children_audit_and_new_counter_stamps_without_restarting_stores()
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        var store = fixture.Records;
        var audit = fixture.Audit;
        Assert.True((await store.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        await audit.EmitAsync(Audit("legacy"));
        await fixture.FinalizeAsync("1.1.0");
        var canonical = "{\"a\":1.50,\"b\":\"café \\u2028\"}";
        var write = NewWrite("performed-proposal") with
        {
            Messages = [new(Records.Message(Id, "message"))],
            Context = [Records.Attachment("context")],
            Snapshots = [new(WeaverInvocationIntegrity.DigestCanonical(canonical), canonical)],
            Keys = [new("call", "key", "value")],
            PausedTurn = new(new(Id, "turn", "proposal", "call", "tool", [], 1)),
            PendingAudits = [Audit("pending")]
        };
        Assert.True((await store.WriteAsync(write)).Succeeded);
        await audit.EmitAsync(Audit("current"));
        Assert.True(await audit.AssociateAsync(Id, "actor-1", "tenant-1"));

        var loaded = (await store.LoadAsync(Id))!;
        Assert.Equal(2, loaded.Version); // The fenced lock increments once; tracked content does not increment again.
        Assert.Equal(canonical, Assert.Single(loaded.Snapshots).Canonical);
        await using var db = fixture.Database.Open();
        Assert.Equal("1.1.0", (await db.Set<WeaverSessionRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverMessageRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverContextRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverProposalRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverDisclosureSnapshotRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverCallKeyRow>().SingleAsync()).SchemaVersion);
        Assert.Equal("1.1.0", (await db.Set<WeaverAuditOwnerRow>().SingleAsync()).SchemaVersion);
        Assert.Equal(["1.0.0", "1.1.0"], await db.Set<WeaverAuditRow>().OrderBy(x => x.Sequence).Select(x => x.SchemaVersion).ToArrayAsync());
        Assert.Equal("1.0.0", (await db.Set<WeaverAuditSequenceRow>().SingleAsync()).SchemaVersion);
        await db.Set<WeaverAuditSequenceRow>().ExecuteDeleteAsync();
        await audit.EmitAsync(Audit("reconstructed"));
        Assert.Equal("1.1.0", (await db.Set<WeaverAuditSequenceRow>().AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paused_or_pending_only_writes_cross_the_shared_interceptor_and_never_lower_a_newer_original_stamp(bool paused)
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        Assert.True((await fixture.Records.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        Assert.Equal(1, await fixture.Database.ExecuteAsync("UPDATE elsa_weaver_sessions SET SchemaVersion = '1.1.0'"));
        var before = WeaverInvocationIntegrity.Digest(await fixture.Records.LoadAsync(Id));
        var write = new WeaverSessionRecordWrite(Id, new(Owner, 1))
        {
            PausedTurn = paused ? new(new(Id, "turn", "proposal", "call", "tool", [], 1)) : null,
            PendingAudits = paused ? null : [Audit("pending")]
        };
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Records.WriteAsync(write));
        Assert.Equal(before, WeaverInvocationIntegrity.Digest(await fixture.Records.LoadAsync(Id)));
        await fixture.FinalizeAsync("1.1.0");
        Assert.True((await fixture.Records.WriteAsync(write)).Succeeded);
        var after = (await fixture.Records.LoadAsync(Id))!;
        Assert.Equal(2, after.Version);
        Assert.Equal(paused, after.PausedTurn is not null);
        Assert.Equal(paused ? 0 : 1, after.PendingAudits.Count);
    }

    [Fact]
    public async Task Opaque_module_values_never_acquire_first_party_schema_meaning()
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        var opaque = JsonSerializer.SerializeToElement(new { ModuleCode = "module.code", Scope = 2, RequiredPrivilegeClasses = new[] { 0 }, OwnerUnavailable = true });
        var result = Result() with { Receipt = Result().Receipt! with { ModuleCode = null, Scope = WeaverEffectScope.ServerPersisted }, Payload = new("module.value", "1", opaque) };
        var record = new WeaverSessionRecord(Records.Session(Id))
        {
            Context = [Records.Attachment("opaque", opaque)],
            Messages = [new(Records.Message(Id, "message", opaque.GetRawText()) with { ToolResults = [result] })],
            Proposals = [new(Records.Proposal(Id, "proposal", WeaverActionProposalStatus.Executed)) { Result = result }],
            Snapshots = [new("legacy-snapshot", opaque.GetRawText())]
        };
        Assert.True((await fixture.Records.CreateAsync(record, Owner)).Succeeded);
        var loaded = (await fixture.Records.LoadAsync(Id))!;
        Assert.Equal(opaque.GetRawText(), Assert.Single(loaded.Snapshots).Canonical);
        Assert.Equal(opaque.GetRawText(), Assert.Single(loaded.Messages).Message.Content);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("message")]
    [InlineData("paused")]
    [InlineData("pending")]
    [InlineData("audit")]
    public async Task Forward_adoption_during_encoding_retries_only_metadata_with_fresh_encoding_and_atomic_rows(string operation)
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (operation is not ("create" or "audit"))
            Assert.True((await fixture.Records.CreateAsync(new(Records.Session(Id)), Owner, cancellation.Token)).Succeeded);
        using var protector = new EncodingBarrier(new DefaultSecretValueProtector(fixture.Database.Keys));
        var codec = new WeaverRecordCodec(protector);
        var records = fixture.Database.RecordStore(codec: codec, contexts: fixture.Contexts);
        var audit = fixture.Database.AuditStore(codec: codec, contexts: fixture.Contexts);
        var write = new WeaverSessionRecordWrite(Id, new(Owner, 1))
        {
            Messages = operation == "message" ? [new(Records.Message(Id, "message"))] : [],
            PausedTurn = operation == "paused" ? new(new(Id, "turn", "proposal", "call", "tool", [], 1)) : null,
            PendingAudits = operation == "pending" ? [Audit("pending")] : null
        };
        // The owning operation has already produced these immutable facts. This barrier is in metadata encoding;
        // there is no owner/tool callback inside the store's retry.
        var pending = Task.Run(async () =>
        {
            if (operation == "create") Assert.True((await records.CreateAsync(new(Records.Session(Id)), Owner, cancellation.Token)).Succeeded);
            else if (operation == "audit") await audit.EmitAsync(Audit("event"), cancellation.Token);
            else Assert.True((await records.WriteAsync(write, cancellation.Token)).Succeeded);
        }, cancellation.Token);
        try
        {
            await protector.Started.Task.WaitAsync(cancellation.Token);
            await fixture.FinalizeAsync("1.1.0");
        }
        finally { protector.Release.Set(); }
        await pending;
        Assert.Equal(2, protector.Calls);
        await using var db = fixture.Database.Open();
        if (operation == "audit")
        {
            var row = await db.Set<WeaverAuditRow>().SingleAsync(cancellation.Token);
            Assert.Equal((1L, "1.1.0"), (row.Sequence, row.SchemaVersion));
            var counter = await db.Set<WeaverAuditSequenceRow>().SingleAsync(cancellation.Token);
            Assert.Equal((1L, "1.1.0"), (counter.Last, counter.SchemaVersion));
        }
        else
        {
            var row = await db.Set<WeaverSessionRow>().SingleAsync(cancellation.Token);
            Assert.Equal(operation == "create" ? 1 : 2, row.Version);
            Assert.Equal("1.1.0", row.SchemaVersion);
            var loaded = (await records.LoadAsync(Id, cancellation.Token))!;
            Assert.Equal(operation == "message" ? 1 : 0, loaded.Messages.Count);
            Assert.Equal(operation == "paused", loaded.PausedTurn is not null);
            Assert.Equal(operation == "pending" ? 1 : 0, loaded.PendingAudits.Count);
            if (operation == "message") Assert.Equal("1.1.0", (await db.Set<WeaverMessageRow>().SingleAsync(cancellation.Token)).SchemaVersion);
        }
    }

    [Fact]
    public async Task Unreadable_finalization_refuses_session_and_audit_mutations_without_falling_back_or_retrying()
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        Assert.True((await fixture.Records.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        await fixture.FinalizeAsync("9.0.0");
        var before = await fixture.Database.ReadAllTextAsync();
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => fixture.Records.WriteAsync(new(Id, new(Owner, 1)) { PendingAudits = [Audit("pending")] }));
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => fixture.Audit.EmitAsync(Audit("event")));
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => fixture.Audit.AssociateAsync(Id, "actor-1", "tenant-1"));
        Assert.Equal(before, await fixture.Database.ReadAllTextAsync());
        Assert.Equal(1, (await fixture.Records.LoadAsync(Id))!.Version);
    }

    [Fact]
    public async Task A_schema_refusal_from_encoding_is_not_replayed_even_when_the_gate_has_advanced()
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        Assert.True((await fixture.Records.CreateAsync(new(Records.Session(Id)), Owner)).Succeeded);
        var expected = new EfSchemaWriteRefusedException("Weaver", "1.1.0", "1.0.0");
        using var barrier = new EncodingBarrier(new DefaultSecretValueProtector(fixture.Database.Keys), expected);
        var store = fixture.Database.RecordStore(codec: new(barrier), contexts: fixture.Contexts);
        var before = WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = Task.Run(() => store.WriteAsync(new(Id, new(Owner, 1))
        { Messages = [new(Records.Message(Id, "message"))] }, cancellation.Token), cancellation.Token);
        try
        {
            await barrier.Started.Task.WaitAsync(cancellation.Token);
            await fixture.FinalizeAsync("1.1.0");
        }
        finally { barrier.Release.Set(); }
        Assert.Same(expected, await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => pending));
        Assert.Equal(1, barrier.Calls);
        Assert.Equal(before, WeaverInvocationIntegrity.Digest(await store.LoadAsync(Id)));
    }

    [Fact]
    public async Task Only_an_exact_readable_forward_adoption_of_this_selected_attempt_is_a_retry()
    {
        using var fixture = await AdmittedDatabase.CreateAsync();
        using var scope = fixture.Contexts.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeaverDbContext>();
        var forward = new EfSchemaWriteRefusedException("Weaver", "1.1.0", "1.0.0");
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", forward));
        await fixture.FinalizeAsync("1.1.0");
        Assert.True(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", forward));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, null, forward));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "9.0.0", new EfSchemaWriteRefusedException("Weaver", "1.1.0", "9.0.0")));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.1.0", forward));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", new EfSchemaWriteRefusedException("Other", "1.1.0", "1.0.0")));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", new EfSchemaWriteRefusedException("Weaver", "1.0.0", "1.0.0")));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.1.0", new EfSchemaWriteRefusedException("Weaver", "1.0.0", "1.1.0")));
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", new EfSchemaFamilyWritesRefusedException("Weaver", "1.1.0", "1.0.0", ["1.0.0", "1.1.0"])));
        await fixture.FinalizeAsync("9.0.0");
        Assert.False(WeaverWriteVersion.IsForwardAdoption(db, "1.0.0", forward));
    }

    private static WeaverSessionRecordWrite NewWrite(string path)
    {
        var result = Result();
        var proposal = new WeaverProposalRecord(Records.Proposal(Id, "proposal", WeaverActionProposalStatus.Executed));
        var execution = new WeaverProposalExecutionResult("proposal", true, "module.resource", "resource", "Ran");
        var message = Records.Message(Id, "message");
        var snapshot = new WeaverDisclosureSnapshot([], [result]);
        var pause = new WeaverPausedTurnRecord(new(Id, "turn", "proposal", "call", "tool", [], 1));
        WeaverSessionRecordWrite WithProposal(WeaverProposalRecord value) => new(Id, new(Owner, 1)) { Proposals = [value] };
        return path switch
        {
            "prepared-classes" => WithProposal(proposal with { Proposal = proposal.Proposal with { PreparedInvocation = Prepared() } }),
            "performed-proposal" => WithProposal(proposal with { Proposal = proposal.Proposal with { Status = WeaverActionProposalStatus.Performed } }),
            "proposal-result-code" => WithProposal(proposal with { Result = result }),
            "proposal-result-scope" => WithProposal(proposal with { Result = result with { Receipt = result.Receipt! with { ModuleCode = null, Scope = WeaverEffectScope.NoPersistedEffect } } }),
            "execution-status" => WithProposal(proposal with { Execution = WeaverResult<WeaverProposalExecutionResult>.Success(execution with { Status = "performed" }) }),
            "execution-classes" => WithProposal(proposal with { Execution = WeaverResult<WeaverProposalExecutionResult>.Success(execution with { ClientInvocation = Prepared() }) }),
            "execution-result" => WithProposal(proposal with { Execution = WeaverResult<WeaverProposalExecutionResult>.Success(execution with { ToolResult = result }) }),
            "execution-error" => WithProposal(proposal with { Execution = new(null, new("module.unavailable", "Unavailable", 503) { OwnerUnavailable = true }) }),
            "message-error" => new(Id, new(Owner, 1)) { Messages = [new(message with { Error = new("module.unavailable", "Unavailable", 503) { OwnerUnavailable = true } })] },
            "message-result-code" => new(Id, new(Owner, 1)) { Messages = [new(message with { ToolResults = [result] })] },
            "message-result-scope" => new(Id, new(Owner, 1)) { Messages = [new(message with { ToolResults = [result with { Receipt = result.Receipt! with { ModuleCode = null, Scope = WeaverEffectScope.NoPersistedEffect } }] })] },
            "message-history" => new(Id, new(Owner, 1)) { Messages = [new(message with { DisclosureHistory = [snapshot] })] },
            "paused-results" => new(Id, new(Owner, 1)) { PausedTurn = pause with { Turn = pause.Turn with { DisclosureResults = [result] } } },
            "paused-history" => new(Id, new(Owner, 1)) { PausedTurn = pause with { Turn = pause.Turn with { DisclosureHistory = [snapshot] } } },
            "snapshot-code" => SnapshotWrite(snapshot),
            "snapshot-scope" => SnapshotWrite(snapshot with { ToolResults = [result with { Receipt = result.Receipt! with { ModuleCode = null, Scope = WeaverEffectScope.NoPersistedEffect } }] }),
            "empty-code" => WithProposal(proposal with { Result = result with { Receipt = result.Receipt! with { ModuleCode = "" } } }),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
    }

    private static WeaverSessionRecordWrite SnapshotWrite(WeaverDisclosureSnapshot snapshot)
    {
        var canonical = WeaverInvocationIntegrity.Canonical(snapshot);
        return new(Id, new(Owner, 1)) { Snapshots = [new(WeaverInvocationIntegrity.DigestCanonical(canonical), canonical)] };
    }

    private static WeaverPreparedToolInvocation Prepared() => new("invocation", Id, "turn", "call", "tool", "1", "module", "actor-1", "tenant-1",
        new("module", "module.resource", "resource", "revision"), JsonSerializer.SerializeToElement(new { }), "digest", WeaverRisk.ReviewRequired,
        new("module.preview", "1", JsonSerializer.SerializeToElement(new { })), WeaverExecutionLocation.Server, true, null, DateTimeOffset.UtcNow.AddMinutes(10))
        { RequiredPrivilegeClasses = [WeaverPrivilegeClass.Activation] };

    private static WeaverToolResult Result() => new("call", "tool", true, "Ran")
    {
        Receipt = new("invocation", new("module", "module.resource", "resource", "revision"), new("module", "module.resource", "resource", "revision"),
            WeaverEffectScope.ServerPersisted, new("module.evidence", "1", JsonSerializer.SerializeToElement(new { ran = true }))) { ModuleCode = "module.ran" }
    };

    private static WeaverAuditEvent Audit(string id) => new(id, WeaverAuditEventKind.MessageAccepted, Id, "actor-1", "Recorded", DateTimeOffset.UtcNow, new Dictionary<string, string>());

    private sealed class AdmittedDatabase : IDisposable
    {
        public WeaverSqliteDatabase Database { get; } = new();
        private readonly ServiceProvider _gateServices;
        private readonly ServiceProvider _contexts;
        private readonly EfSchemaModuleGate _gate;
        public ServiceProvider Contexts => _contexts;
        public EfWeaverSessionRecordStore Records { get; }
        public EfWeaverAuditStore Audit { get; }

        private AdmittedDatabase()
        {
            var gates = new EfSchemaFinalizationGates();
            _gateServices = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            _contexts = WeaverSqliteDatabase.Contexts(Database.ConnectionString, builder => builder.UseApplicationServiceProvider(_gateServices));
            _gate = new(EfSchemaModuleFamilies.ForContext(typeof(WeaverDbContext))!, null, new(), new());
            gates.Register(typeof(WeaverDbContext), _gate);
            Records = Database.RecordStore(contexts: _contexts);
            Audit = Database.AuditStore(contexts: _contexts);
        }

        public static async Task<AdmittedDatabase> CreateAsync()
        {
            var fixture = new AdmittedDatabase();
            try
            {
                await using var db = fixture.Database.Open();
                await fixture._gate.ActivateAsync(db);
                Assert.Equal("1.0.0", fixture._gate.StateOf("Weaver")!.WriteVersion);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        public async Task FinalizeAsync(string version)
        {
            await using var db = Database.Open();
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new(db), "Weaver", ["1.0.0", "1.1.0", "9.0.0"], version);
            await _gate.RefreshAsync(db);
        }

        public void Dispose()
        {
            _contexts.Dispose();
            _gateServices.Dispose();
            Database.Dispose();
        }
    }

    private sealed class EncodingBarrier(ISecretValueProtector inner, Exception? refusal = null) : ISecretValueProtector, IDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public string Protect(string value)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Started.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("The schema encoding barrier was not released.");
                if (refusal is not null) throw refusal;
            }
            return inner.Protect(value);
        }

        public string Unprotect(string value) => inner.Unprotect(value);
        public void Dispose() => Release.Dispose();
    }
}
