using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 180's chain rules (FR-004, FR-005, FR-007, FR-009, FR-021) on synthetic families, since every first-party family
/// still has one version. The invariants under test: a read never returns data for a version it cannot upcast, the
/// readable set a declaration reports is exactly the set a read accepts, and a row is either at its stamped version or
/// wholly upcast to the current one - its columns are exactly its table's declared content columns before and after every
/// step (#2144).
/// </summary>
public sealed class EfSchemaChainTests
{
    private static readonly EfSchemaChain Orders = Declare("Orders", "3", Step<OneToTwo>(), Step<TwoToThree>());

    [Fact]
    public void A_contiguous_chain_reads_the_current_version_and_every_predecessor_in_chain_order()
    {
        Assert.Equal(["1", "2", "3"], Orders.ReadableVersions);
        Assert.Empty(Orders.Defects);
        Orders.EnsureSound();
    }

    [Fact]
    public void A_row_two_versions_behind_is_upcast_one_step_at_a_time_in_chain_order() =>
        Assert.Equal(["1->2", "2->3"], Steps(Orders.Upcast("1", RowOf("""{"steps":[]}"""))));

    [Fact]
    public void A_row_one_version_behind_runs_only_the_last_step() =>
        Assert.Equal(["2->3"], Steps(Orders.Upcast("2", RowOf("""{"steps":[]}"""))));

    /// <summary>FR-021: at the current version no upcaster runs, so the row comes back as the very same instance.</summary>
    [Fact]
    public void A_row_at_the_current_version_runs_no_upcaster()
    {
        var failing = Declare("Strict", "2", Step<Failing>());
        var row = RowOf("anything");

        Assert.Same(row, failing.Upcast("2", row));
    }

    /// <summary>
    /// #2144: a step sees the whole row, so one step can move data from one content column into another, including one
    /// that is null because the row was written before anything was stored in it.
    /// </summary>
    [Fact]
    public void A_step_moves_data_between_two_content_columns_of_one_row()
    {
        var moving = Declare("Moving", "2", Step<MoveNoteToNotes>());

        var row = moving.Upcast("1", RowOf("""{"note":"hello","total":1}""", null));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"total":1}"""), JsonNode.Parse(row[Content]!)));
        Assert.Equal("hello", row[Notes]);
    }

    /// <summary>FR-007 and US2: above the chain, below it and unstamped are all skew, and never corruption.</summary>
    [Theory]
    [InlineData("4")]
    [InlineData("0")]
    [InlineData(null)]
    [InlineData("")]
    public void A_stamp_outside_the_readable_set_is_skew_never_corruption(string? stamp)
    {
        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => Orders.Upcast(stamp, RowOf("{}")));

        Assert.Equal("Orders", skew.Family);
        Assert.Equal(stamp, skew.Found);
        Assert.Equal(["1", "2", "3"], skew.ReadableVersions);
        Assert.False(Orders.IsReadable(stamp));
    }

    /// <summary>FR-006: the stamp is settled before anything else about the row, so an unreadable stamp is skew even when the columns are wrong too.</summary>
    [Fact]
    public void An_unreadable_stamp_is_skew_before_the_columns_are_judged() =>
        Assert.Throws<EfSchemaVersionSkewException>(() => Orders.Upcast("4", new EfSchemaRowContent(typeof(Row), (Content, "{}"))));

    /// <summary>
    /// #2144: a read passes exactly the declared content columns of the row's table, and the chain refuses anything else
    /// at every readable version, the current one included, where no step would run. Refusing only when a step runs would
    /// let a store that reads one column past the others pass every test while every family has one version.
    /// </summary>
    [Theory]
    [MemberData(nameof(RowsNotAsDeclared))]
    public void A_read_that_is_not_its_tables_declared_content_columns_is_refused_at_every_version(string name, EfSchemaRowContent row)
    {
        foreach (var stamp in Orders.ReadableVersions)
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => Orders.Upcast(stamp, row));
            Assert.True(refusal.Message.Contains("'Orders'", StringComparison.Ordinal) && refusal.Message.Contains("#2144", StringComparison.Ordinal),
                $"{name} at '{stamp}': {refusal.Message}");
        }
    }

    public static TheoryData<string, EfSchemaRowContent> RowsNotAsDeclared() => new()
    {
        { "a row missing a declared column", new EfSchemaRowContent(typeof(Row), (Content, """{"steps":[]}""")) },
        { "a row with an undeclared column", new EfSchemaRowContent(typeof(Row), (Content, """{"steps":[]}"""), (Notes, null), ("Extra", null)) },
        { "a row of a table the family declares no content for", new EfSchemaRowContent(typeof(OtherTable), (Content, """{"steps":[]}"""), (Notes, null)) }
    };

    /// <summary>
    /// #2144: a step returns exactly the row's declared content columns of the same table. One that adds a column, drops
    /// one, answers for another table or returns nothing leaves the row untrustworthy, so the row is corrupt (FR-009) and
    /// never returned half upgraded.
    /// </summary>
    [Theory]
    [MemberData(nameof(StepsThatReshapeTheRow))]
    public void A_step_that_does_not_return_the_rows_declared_columns_reports_corruption(string name, EfSchemaUpcasterDescriptor step, string fault)
    {
        var chain = Declare("Reshaped", "2", step);

        var corrupt = Assert.Throws<InvalidDataException>(() => chain.Upcast("1", RowOf("{}", "notes")));

        Assert.True(corrupt.Message.Contains("'Reshaped'", StringComparison.Ordinal) && corrupt.Message.Contains(fault, StringComparison.Ordinal) &&
                    corrupt.Message.Contains("corrupt", StringComparison.Ordinal),
            $"{name}: {corrupt.Message}");
    }

    public static TheoryData<string, EfSchemaUpcasterDescriptor, string> StepsThatReshapeTheRow() => new()
    {
        { "a step that adds a column", Step<AddsAColumn>(), "returned columns 'Content', 'Extra', 'Notes'" },
        { "a step that drops a column", Step<DropsAColumn>(), "returned columns 'Content'" },
        { "a step that sets a column the row does not hold", Step<SetsAnUndeclaredColumn>(), "failed" },
        { "a step that answers for another table", Step<AnswersForAnotherTable>(), $"returned a row of {nameof(OtherTable)}" },
        { "a step that returns nothing", Step<ReturnsNull>(), "returned no row" }
    };

    /// <summary>
    /// US2, scenario 2: a chain that skips 1 to 2 reads only what it reaches from the current version without the gap.
    /// A row below the gap is skew; it is never bridged by applying the upcasters that do exist.
    /// </summary>
    [Fact]
    public void A_gap_is_never_bridged_and_a_row_below_it_is_skew()
    {
        var gapped = Declare("Gapped", "3", Step<ZeroToOne>(), Step<TwoToThree>());

        Assert.Equal(["2", "3"], gapped.ReadableVersions);
        Assert.Contains(gapped.Defects, defect => defect.Contains("gap", StringComparison.Ordinal) && defect.Contains("'1'", StringComparison.Ordinal) && defect.Contains("'2'", StringComparison.Ordinal));
        Assert.Throws<EfSchemaVersionSkewException>(() => gapped.Upcast("1", RowOf("""{"steps":[]}""")));
        Assert.Throws<EfSchemaVersionSkewException>(() => gapped.Upcast("0", RowOf("""{"steps":[]}""")));
        Assert.Equal(["2->3"], Steps(gapped.Upcast("2", RowOf("""{"steps":[]}"""))));
    }

    /// <summary>FR-005: every malformed chain is refused where the family is registered, naming the family and the fault.</summary>
    [Theory]
    [MemberData(nameof(MalformedChains))]
    public void A_malformed_chain_is_refused_at_registration(string name, EfSchemaFamilyDescriptor declaration, string[] readable, string fault)
    {
        var chain = EfSchemaChain.For(declaration);

        Assert.Equal(readable, chain.ReadableVersions);
        Assert.Equal(readable, declaration.ReadableVersions);
        var refusal = Assert.Throws<InvalidOperationException>(chain.EnsureSound);
        Assert.Contains($"'{declaration.Name}'", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Message.Contains(fault, StringComparison.Ordinal), $"{name}: {refusal.Message}");
    }

    public static TheoryData<string, EfSchemaFamilyDescriptor, string[], string> MalformedChains() => new()
    {
        { "a gap", Descriptor("Gap", "3", Step<ZeroToOne>(), Step<TwoToThree>()), ["2", "3"], "gap" },
        { "a chain that stops short of the current version", Descriptor("Short", "4", Step<OneToTwo>(), Step<TwoToThree>()), ["4"], "not at the current version '4'" },
        { "a cycle", Descriptor("Cycle", "2", Step<TwoToOne>(), Step<OneToTwo>()), ["1", "2"], "more than once" },
        { "a branch", Descriptor("Branch", "3", Step<OneToTwo>(), Step<OneToThree>()), ["1", "3"], "more than once" },
        { "a duplicated step", Descriptor("Twice", "3", Step<TwoToThree>(), Step<TwoToThree>()), ["2", "3"], "more than once" },
        { "an upcaster naming no versions", Descriptor("Unnamed", "2", Step<Unversioned>()), ["2"], "names no source and target version" },
        { "an upcaster to itself", Descriptor("Self", "2", Step<TwoToTwo>()), ["2"], "to itself" },
        { "a type that is no upcaster", Descriptor("NotAnUpcaster", "2", Step<NotAnUpcaster>()), ["2"], "implements" },
        { "an upcaster without a parameterless constructor", Descriptor("Injected", "2", Step<Injected>()), ["2"], "parameterless constructor" },
        { "an abstract upcaster", Descriptor("Abstract", "2", Step<AbstractUpcaster>()), ["2"], "concrete class" }
    };

    /// <summary>
    /// A chain the build refuses still never credits a version it cannot reach: the readable set stops at the first
    /// malformed step, walking back from the current version, and the steps below it are never constructed.
    /// </summary>
    [Fact]
    public void A_malformed_step_hides_every_version_below_it()
    {
        var chain = Declare("Hidden", "3", Step<ZeroToOne>(), Step<Unversioned>(), Step<TwoToThree>());

        Assert.Equal(["2", "3"], chain.ReadableVersions);
        Assert.Throws<EfSchemaVersionSkewException>(() => chain.EnsureReadable("1"));
        Assert.Throws<EfSchemaVersionSkewException>(() => chain.EnsureReadable("0"));
    }

    /// <summary>FR-009: an upcaster that fails on a row whose stamp is readable reports the row as corrupt, not as skew.</summary>
    [Fact]
    public void An_upcaster_that_fails_on_a_readable_row_reports_corruption()
    {
        var chain = Declare("Strict", "2", Step<Failing>());

        var corrupt = Assert.Throws<InvalidDataException>(() => chain.Upcast("1", RowOf("{}")));

        Assert.Contains("'Strict'", corrupt.Message, StringComparison.Ordinal);
        Assert.Contains($"a row of {nameof(Row)}", corrupt.Message, StringComparison.Ordinal);
        Assert.Contains("corrupt", corrupt.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(corrupt.InnerException);
    }

    /// <summary>FR-008: a projection introduced at a version applies to rows stamped at it or later, in chain order.</summary>
    [Fact]
    public void A_stamp_is_ordered_by_the_chain_not_by_its_label()
    {
        var labelled = Declare("Labelled", "10", Step<BToNine>(), Step<NineToTen>());

        Assert.Equal(["b", "9", "10"], labelled.ReadableVersions);
        Assert.True(labelled.IsAtOrAfter("10", "9"));
        Assert.True(labelled.IsAtOrAfter("9", "9"));
        Assert.False(labelled.IsAtOrAfter("b", "9"));
        Assert.Throws<EfSchemaVersionSkewException>(() => labelled.IsAtOrAfter("8", "9"));
        Assert.Throws<ArgumentOutOfRangeException>(() => labelled.IsAtOrAfter("10", "8"));
    }

    /// <summary>
    /// Concurrent readers share one chain and one instance of each upcaster; since an upcaster is a pure function of its
    /// input, every reader gets the same row, however many read at once.
    /// </summary>
    [Fact]
    public async Task Concurrent_readers_upcast_the_same_row_to_the_same_content()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => Orders.Upcast("1", RowOf("""{"steps":[]}""", "notes")))));

        Assert.Single(results.Select(row => (row[Content], row[Notes])).Distinct());
        Assert.Equal(["1->2", "2->3"], Steps(results[0]));
    }

    /// <summary>SC-007, within one process: two separately resolved chains produce byte-identical output.</summary>
    [Fact]
    public void Two_resolutions_of_one_declaration_upcast_to_byte_identical_content()
    {
        var first = EfSchemaChain.For(Descriptor("Orders", "3", Step<OneToTwo>(), Step<TwoToThree>()));
        var second = EfSchemaChain.For(Descriptor("Orders", "3", Step<OneToTwo>(), Step<TwoToThree>()));

        Assert.Equal(
            first.Upcast("1", RowOf("""{"steps":[],"b":1,"a":2}""", "n")).Columns,
            second.Upcast("1", RowOf("""{"steps":[],"b":1,"a":2}""", "n")).Columns);
    }

    private static string[] Steps(EfSchemaRowContent row) =>
        JsonNode.Parse(row[Content]!)!["steps"]!.AsArray().Select(step => step!.GetValue<string>()).ToArray();

    private static EfSchemaRowContent Mark(EfSchemaRowContent row, string step)
    {
        var node = JsonNode.Parse(row[Content]!)!.AsObject();
        node["steps"]!.AsArray().Add(step);
        return row.With(Content, node.ToJsonString());
    }

    private sealed class OtherTable;

    [EfSchemaUpcaster("0", "1")]
    private sealed class ZeroToOne : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => Mark(row, "0->1");
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class OneToTwo : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => Mark(row, "1->2");
    }

    [EfSchemaUpcaster("2", "3")]
    private sealed class TwoToThree : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => Mark(row, "2->3");
    }

    [EfSchemaUpcaster("1", "3")]
    private sealed class OneToThree : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("2", "1")]
    private sealed class TwoToOne : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("2", "2")]
    private sealed class TwoToTwo : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("9", "10")]
    private sealed class NineToTen : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("b", "9")]
    private sealed class BToNine : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    private sealed class Unversioned : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class NotAnUpcaster;

    [EfSchemaUpcaster("1", "2")]
    private sealed class Injected(TimeProvider clock) : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row.With(Content, clock.GetUtcNow().ToString("O"));
    }

    [EfSchemaUpcaster("1", "2")]
    private abstract class AbstractUpcaster : IEfSchemaUpcaster
    {
        public abstract EfSchemaRowContent Upcast(EfSchemaRowContent row);
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class Failing : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => throw new FormatException("not a version-1 document");
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class ReturnsNull : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => null!;
    }

    /// <summary>Moves the document's note into the notes column: one step, two columns.</summary>
    [EfSchemaUpcaster("1", "2")]
    private sealed class MoveNoteToNotes : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row)
        {
            var document = JsonNode.Parse(row[Content]!)!.AsObject();
            var note = document["note"]!.GetValue<string>();
            document.Remove("note");
            return row.With(Content, document.ToJsonString()).With(Notes, note);
        }
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class AddsAColumn : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) =>
            new(row.Entity, (Content, row[Content]), (Notes, row[Notes]), ("Extra", "{}"));
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class DropsAColumn : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => new(row.Entity, (Content, row[Content]));
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class SetsAnUndeclaredColumn : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row.With("Extra", "{}");
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class AnswersForAnotherTable : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => new(typeof(OtherTable), (Content, row[Content]), (Notes, row[Notes]));
    }
}

/// <summary>A row's content as the chain passes it: immutable, ordered, and changed one declared column at a time.</summary>
public sealed class EfSchemaRowContentTests
{
    [Fact]
    public void With_changes_one_column_and_keeps_every_other_as_it_was()
    {
        var row = RowOf("content", "notes");

        var changed = row.With(Notes, "changed");

        Assert.Equal(("content", "changed"), (changed[Content], changed[Notes]));
        Assert.Equal(("content", "notes"), (row[Content], row[Notes]));
        Assert.Equal(row.Entity, changed.Entity);
    }

    [Fact]
    public void With_refuses_a_column_the_row_does_not_hold() =>
        Assert.Throws<ArgumentException>(() => RowOf("content").With("Extra", "{}"));

    [Fact]
    public void Reading_a_column_the_row_does_not_hold_throws() =>
        Assert.Throws<KeyNotFoundException>(() => RowOf("content")["Extra"]);

    [Fact]
    public void A_column_named_twice_is_refused() =>
        Assert.Throws<ArgumentException>(() => new EfSchemaRowContent(typeof(Row), (Content, "a"), (Content, "b")));

    /// <summary>FR-019: an upcaster that walks the columns sees them in one order, whatever order the reader built the row in.</summary>
    [Fact]
    public void Columns_enumerate_in_ordinal_order_whatever_order_they_were_given_in() =>
        Assert.Equal(
            ["A", "b", "c"],
            new EfSchemaRowContent(typeof(Row), ("c", null), ("A", null), ("b", null)).With("b", "x").Columns.Keys);
}
