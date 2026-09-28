using System.Text.Json;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;
using static Elsa.Persistence.EntityFramework.Tests.SyntheticOrders;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 180's read and write paths end to end, through a store written the way every first-party store now is, over the
/// <see cref="SyntheticOrders"/> family. A row stamped 1 is read through the chain, left untouched by the read, and
/// upgraded when it is next written; rows the chain cannot place are skew, and a readable row whose content is damaged
/// is corruption. FR-022's fixture proofs for its two upcasters are <see cref="AddCurrencyProof"/> and
/// <see cref="AddLinesProof"/>.
/// </summary>
public sealed class EfSchemaChainStoreTests : IAsyncDisposable
{
    private static readonly Order Expected = new("order-1", 42, "EUR", []);

    private readonly SyntheticOrdersDatabase orders = new();

    /// <summary>US1, scenario 1: a version-1 row reads as the domain value the version-3 row of the same order does.</summary>
    [Fact]
    public async Task A_row_two_versions_behind_reads_as_the_current_version_would()
    {
        await orders.PutAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");
        await orders.PutAsync("order-3", "3", "EUR", """{"Id":"order-3","Total":42,"Currency":"EUR","Lines":[]}""");

        Assert.Equal(Expected, await orders.Store().ReadAsync("order-1"));
        Assert.Equal(Expected with { Id = "order-3" }, await orders.Store().ReadAsync("order-3"));
    }

    /// <summary>US1, scenario 2: a read never rewrites a row, so its stamp and its stored content stay exactly as they were.</summary>
    [Fact]
    public async Task Reading_an_older_row_leaves_its_stamp_and_bytes_unchanged()
    {
        const string stored = """{"Id":"order-1","Total":42}""";
        await orders.PutAsync("order-1", "1", null, stored);

        _ = await orders.Store().ReadAsync("order-1");

        Assert.Equal(("1", (string?)null, stored), await orders.RawAsync("order-1"));
    }

    /// <summary>
    /// FR-013 and FR-014: the next write stamps the current version and writes the current format, which is how a row
    /// moves forward; the row then reads without any upcaster running.
    /// </summary>
    [Fact]
    public async Task A_row_read_two_versions_behind_is_upgraded_when_it_is_next_written()
    {
        await orders.PutAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");

        var read = await orders.Store().ReadAsync("order-1");
        await orders.Store().SaveAsync(read with { Total = 43 });

        var (stamp, currency, content) = await orders.RawAsync("order-1");
        Assert.Equal(Chain.CurrentVersion, stamp);
        Assert.Equal("EUR", currency);
        Assert.Equal(Expected with { Total = 43 }, JsonSerializer.Deserialize<Order>(content));
        var calls = UpcasterCalls.Snapshot();
        Assert.Equal(Expected with { Total = 43 }, await orders.Store().ReadAsync("order-1"));
        Assert.Equal(calls, UpcasterCalls.Snapshot());
    }

    /// <summary>US1, scenario 3 and FR-021: a row at the current version is read without running any upcaster.</summary>
    [Fact]
    public async Task A_row_at_the_current_version_runs_no_upcaster()
    {
        await orders.PutAsync("order-3", "3", "EUR", """{"Id":"order-3","Total":42,"Currency":"EUR","Lines":[]}""");
        var calls = UpcasterCalls.Snapshot();

        _ = await orders.Store().ReadAsync("order-3");

        Assert.Equal(calls, UpcasterCalls.Snapshot());
    }

    /// <summary>US2, scenarios 1 and 3: above the chain, below it and unstamped are skew, and never reported as corruption.</summary>
    [Theory]
    [InlineData("4")]
    [InlineData("0")]
    [InlineData("")]
    public async Task A_row_the_chain_cannot_place_is_skew_and_never_corruption(string stamp)
    {
        await orders.PutAsync("order-x", stamp, "EUR", "not-json");

        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => orders.Store().ReadAsync("order-x"));

        Assert.Equal(Family, skew.Family);
        Assert.Equal(stamp, skew.Found);
        Assert.Equal(["1", "2", "3"], skew.ReadableVersions);
    }

    /// <summary>US2, scenario 2 at read time: below a gap is skew, whatever upcasters the chain declares further down.</summary>
    [Fact]
    public async Task A_row_below_a_gap_is_skew_while_one_above_it_still_reads()
    {
        var gapped = Declare(Family, "3", Step<CurrencyFromNothing>(), Step<AddLines>());
        await orders.PutAsync("order-1", "1", null, """{"Id":"order-1","Total":42}""");
        await orders.PutAsync("order-2", "2", "EUR", """{"Id":"order-2","Total":42,"Currency":"EUR"}""");

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => orders.Store(gapped).ReadAsync("order-1"));
        Assert.Equal(Expected with { Id = "order-2" }, await orders.Store(gapped).ReadAsync("order-2"));
    }

    /// <summary>US2, scenario 4: a readable version whose content is not that version's shape is a damaged row.</summary>
    [Fact]
    public async Task A_readable_row_whose_content_is_damaged_is_corruption()
    {
        await orders.PutAsync("order-1", "1", null, "not-json");

        await Assert.ThrowsAsync<InvalidDataException>(() => orders.Store().ReadAsync("order-1"));
    }

    /// <summary>
    /// FR-008: the currency projection exists from version 2 on. A version-1 row carries none and passes; a version-1 row
    /// that carries one, or a version-2 row that lacks it, fails the integrity clause for its own stamped version.
    /// </summary>
    [Theory]
    [InlineData("1", "EUR", """{"Id":"order-1","Total":42}""")]
    [InlineData("2", null, """{"Id":"order-1","Total":42,"Currency":"EUR"}""")]
    public async Task A_projection_is_checked_against_the_definition_of_the_rows_stamped_version(string stamp, string? currency, string content)
    {
        await orders.PutAsync("order-1", stamp, currency, content);

        await Assert.ThrowsAsync<InvalidDataException>(() => orders.Store().ReadAsync("order-1"));
    }

    /// <summary>FR-018: a write never overwrites a row whose stamp it cannot read, so a newer row's content is never lost.</summary>
    [Fact]
    public async Task A_write_refuses_to_replace_a_row_it_cannot_read()
    {
        const string newer = """{"Id":"order-1","Total":42,"Currency":"EUR","Lines":[],"Gift":true}""";
        await orders.PutAsync("order-1", "4", "EUR", newer);

        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => orders.Store().SaveAsync(Expected));

        Assert.Equal(("4", "EUR", newer), await orders.RawAsync("order-1"));
    }

    [Fact]
    public async Task Concurrent_readers_of_an_older_row_all_see_the_current_value_and_leave_the_row_as_it_was()
    {
        const string stored = """{"Id":"order-1","Total":42}""";
        await orders.PutAsync("order-1", "1", null, stored);

        var reads = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => orders.Store().ReadAsync("order-1"))));

        Assert.All(reads, read => Assert.Equal(Expected, read));
        Assert.Equal(("1", (string?)null, stored), await orders.RawAsync("order-1"));
    }

    public ValueTask DisposeAsync() => orders.DisposeAsync();

    /// <summary>An upcaster into a version below the gap, so the chain declares something under it that must stay unused.</summary>
    [EfSchemaUpcaster("0", "1")]
    private sealed class CurrencyFromNothing : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }
}
