using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaGate;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The write check every module context carries (spec 181, FR-009 and FR-012; spec 180, FR-013, FR-015 and FR-016):
/// every row a save writes carries its family's write version, a row stamped later is refused rather than written, a
/// family whose finalized version this host cannot read accepts no write at all, and a row read at a later version is
/// written only once the record confirms it. Each refusal is checked to have saved nothing, synchronously and not.
/// </summary>
public sealed class EfSchemaWriteGateInterceptorTests : IAsyncLifetime
{
    private readonly TemporarySqliteDatabase database = new("schema-write-gate");
    private readonly FakeFleetState fleet = new();
    private readonly EfSchemaFinalizationGates gates = new();
    private readonly List<DbContext> contexts = [];

    public async Task InitializeAsync() => await Context().Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        foreach (var context in contexts)
            await context.DisposeAsync();
        await database.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_row_at_the_write_version_is_written_and_one_at_the_hosts_newer_current_version_is_refused(bool synchronous)
    {
        await AdmitAsync("2", heldAt: "1");

        await SaveAsync(Row("a", "1"), synchronous);
        var refusal = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => SaveAsync(Row("b", "2"), synchronous));

        Assert.Equal((Family, "1", "2"), (refusal.Family, refusal.WriteVersion, refusal.RequiredVersion));
        Assert.Equal(["a"], await IdsAsync());
    }

    [Fact]
    public async Task Once_the_version_is_finalized_and_refreshed_the_same_host_writes_it()
    {
        var gate = await AdmitAsync("2", heldAt: "1");
        await ReleaseAndFinalizeAsync(gate);

        await SaveAsync(Row("a", "2"), synchronous: false);

        Assert.Equal(["a"], await IdsAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_family_whose_finalized_version_this_host_cannot_read_refuses_every_write_and_delete(bool synchronous)
    {
        var gate = await AdmitAsync("1");
        await SaveAsync(Row("kept", "1"), synchronous);
        await FinalizeElsewhereAsync("2");
        await gate.RefreshAsync(Context());

        var insert = await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => SaveAsync(Row("new", "1"), synchronous));
        Assert.Equal("2", insert.RequiredVersion);
        Assert.Equal(["1"], insert.ReadableVersions);

        var context = Context();
        context.Rows.Remove(await context.Rows.SingleAsync());
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => synchronous ? Task.FromResult(context.SaveChanges()) : context.SaveChangesAsync());
        Assert.Equal(["kept"], await IdsAsync());
    }

    [Fact]
    public async Task A_refused_family_leaves_the_modules_other_families_writable()
    {
        var gate = await AdmitAsync("1");
        await FinalizeElsewhereAsync("2");
        await gate.RefreshAsync(Context());

        var context = Context();
        context.Others.Add(new OtherRow { Id = "other", SchemaVersion = "1" });
        await context.SaveChangesAsync();

        Assert.True(gate.StateOf(Family)!.WritesRefused);
        Assert.False(gate.StateOf(OtherFamily)!.WritesRefused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_row_read_at_a_later_version_is_rewritten_only_once_the_record_confirms_that_version(bool synchronous)
    {
        // host-a reads 1 to 3 but was admitted while 2 was held, so it writes 1; host-b finalizes 2 and writes a row at 2.
        var gate = await AdmitAsync("3", heldAt: "1");
        await InsertRawAsync("written-by-b", "2");

        // Unconfirmed: the record still says 1, so rewriting the row at 1 would lower its stamp and at 2 would outrun it.
        var context = Context();
        var row = await context.Rows.SingleAsync();
        row.Content = "changed";
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => synchronous ? Task.FromResult(context.SaveChanges()) : context.SaveChangesAsync());

        // Confirmed: 2 is finalized now, and the save re-reads the record itself rather than waiting for a refresh.
        await ReleaseAndFinalizeAsync(gate, refresh: false);
        Assert.Equal("1", gate.StateOf(Family)!.WriteVersion);
        var confirmed = Context();
        var reread = await confirmed.Rows.SingleAsync();
        reread.Content = "changed";
        if (synchronous) confirmed.SaveChanges(); else await confirmed.SaveChangesAsync();

        Assert.Equal("2", gate.StateOf(Family)!.WriteVersion);
        Assert.Equal("changed", (await Context().Rows.SingleAsync()).Content);
    }

    [Fact]
    public async Task A_context_no_module_declares_and_no_gate_admitted_is_left_unchecked()
    {
        // No gate is registered for this context: its families are unknown to the check, so it checks nothing, which is
        // why every real module context's gate is registered at Prepare, before anything writes.
        await SaveAsync(Row("unchecked", "3"), synchronous: false);
        Assert.Equal(["unchecked"], await IdsAsync());
    }

    private async Task<EfSchemaModuleGate> AdmitAsync(string current, string? heldAt = null)
    {
        if (heldAt is not null)
        {
            var created = await new EfSchemaFinalizationStore(Context()).GetOrCreateAsync(Family, heldAt, ["1", "2", "3"], SchemaFinalizationActor.OfOperator("ops"));
            await new EfSchemaFinalizationStore(Context()).PlaceHoldAsync(Family, created.Revision, null, "held for the test", "ops", ["1", "2", "3"]);
        }

        var readable = Enumerable.Range(1, int.Parse(current)).Select(version => version.ToString()).ToArray();
        var member = fleet.Add(new FakeMember("host-a").Reading(Family, readable).Reading(OtherFamily, "1"));
        var gate = Gate(Families(current), new FakeFleet(fleet, member));
        await gate.ActivateAsync(Context());
        gates.Register(typeof(GateContext), gate);
        return gate;
    }

    private async Task ReleaseAndFinalizeAsync(EfSchemaModuleGate gate, bool refresh = true)
    {
        var store = new EfSchemaFinalizationStore(Context());
        var record = (await store.FindAsync(Family))!;
        record = (await store.ReleaseHoldAsync(Family, record.Revision, null, "ops")).Record;
        record = (await store.RecordIntentAsync(Family, record.Revision, "2", ["1", "2", "3"], new("host-b", "b"))).Record;
        await store.CommitIntentAsync(Family, record.Revision, ["1", "2", "3"], new("host-b", "b"));
        if (refresh)
            await gate.RefreshAsync(Context());
    }

    private async Task FinalizeElsewhereAsync(string version)
    {
        var store = new EfSchemaFinalizationStore(Context());
        var record = (await store.FindAsync(Family))!;
        record = (await store.RecordIntentAsync(Family, record.Revision, version, ["1", "2", "3"], new("host-b", "b"))).Record;
        await store.CommitIntentAsync(Family, record.Revision, ["1", "2", "3"], new("host-b", "b"));
    }

    private async Task InsertRawAsync(string id, string stamp)
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("INSERT INTO gate_rows (Id, SchemaVersion, Content) VALUES ({0}, {1}, 'original')", id, stamp);
    }

    private async Task SaveAsync(GateRow row, bool synchronous)
    {
        var context = Context();
        context.Rows.Add(row);
        if (synchronous)
            context.SaveChanges();
        else
            await context.SaveChangesAsync();
    }

    private async Task<string[]> IdsAsync() => await Context().Rows.AsNoTracking().Select(row => row.Id).OrderBy(id => id).ToArrayAsync();

    private static GateRow Row(string id, string stamp) => new() { Id = id, SchemaVersion = stamp, Content = id };

    private GateContext Context()
    {
        var context = SchemaGate.Context(database.ConnectionString, gates);
        contexts.Add(context);
        return context;
    }
}
