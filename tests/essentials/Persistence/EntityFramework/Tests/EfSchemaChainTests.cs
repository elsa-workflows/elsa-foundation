using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.SchemaChains;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 180's chain rules (FR-004, FR-005, FR-007, FR-009, FR-021) on synthetic families, since every first-party family
/// still has one version. The invariant under test: a read never returns data for a version it cannot upcast, and the
/// readable set a declaration reports is exactly the set a read accepts.
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
    public void A_row_two_versions_behind_is_upcast_one_step_at_a_time_in_chain_order()
    {
        var content = Orders.Upcast("1", "orders", "Content", """{"steps":[]}""");

        Assert.Equal(["1->2", "2->3"], Steps(content));
    }

    [Fact]
    public void A_row_one_version_behind_runs_only_the_last_step() =>
        Assert.Equal(["2->3"], Steps(Orders.Upcast("2", "orders", "Content", """{"steps":[]}""")));

    /// <summary>FR-021: at the current version no upcaster runs, so the content comes back as the very same string.</summary>
    [Fact]
    public void A_row_at_the_current_version_runs_no_upcaster()
    {
        var failing = Declare("Strict", "2", Step<Failing>());
        const string content = "anything";

        Assert.Same(content, failing.Upcast("2", "t", "c", content));
    }

    [Fact]
    public void A_null_column_is_not_upcast() =>
        Assert.Null(Declare("Strict", "2", Step<Failing>()).Upcast("1", "t", "c", null));

    /// <summary>FR-007 and US2: above the chain, below it and unstamped are all skew, and never corruption.</summary>
    [Theory]
    [InlineData("4")]
    [InlineData("0")]
    [InlineData(null)]
    [InlineData("")]
    public void A_stamp_outside_the_readable_set_is_skew_never_corruption(string? stamp)
    {
        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => Orders.Upcast(stamp, "orders", "Content", "{}"));

        Assert.Equal("Orders", skew.Module);
        Assert.Equal(stamp, skew.Found);
        Assert.Equal(["1", "2", "3"], skew.ReadableVersions);
        Assert.False(Orders.IsReadable(stamp));
    }

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
        Assert.Throws<EfSchemaVersionSkewException>(() => gapped.Upcast("1", "t", "c", """{"steps":[]}"""));
        Assert.Throws<EfSchemaVersionSkewException>(() => gapped.Upcast("0", "t", "c", """{"steps":[]}"""));
        Assert.Equal(["2->3"], Steps(gapped.Upcast("2", "t", "c", """{"steps":[]}""")));
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

        var corrupt = Assert.Throws<InvalidDataException>(() => chain.Upcast("1", "orders", "Content", "{}"));

        Assert.Contains("'Strict'", corrupt.Message, StringComparison.Ordinal);
        Assert.Contains("orders.Content", corrupt.Message, StringComparison.Ordinal);
        Assert.Contains("corrupt", corrupt.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(corrupt.InnerException);
    }

    [Fact]
    public void An_upcaster_that_returns_nothing_reports_corruption() =>
        Assert.Throws<InvalidDataException>(() => Declare("Empty", "2", Step<ReturnsNull>()).Upcast("1", "t", "c", "{}"));

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
    /// input, every reader gets the same content, however many read at once.
    /// </summary>
    [Fact]
    public async Task Concurrent_readers_upcast_the_same_row_to_the_same_content()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => Orders.Upcast("1", "orders", "Content", """{"steps":[]}"""))));

        Assert.Single(results.Distinct(StringComparer.Ordinal));
        Assert.Equal(["1->2", "2->3"], Steps(results[0]));
    }

    /// <summary>SC-007, within one process: two separately resolved chains produce byte-identical output.</summary>
    [Fact]
    public void Two_resolutions_of_one_declaration_upcast_to_byte_identical_content()
    {
        var first = EfSchemaChain.For(Descriptor("Orders", "3", Step<OneToTwo>(), Step<TwoToThree>()));
        var second = EfSchemaChain.For(Descriptor("Orders", "3", Step<OneToTwo>(), Step<TwoToThree>()));

        Assert.Equal(first.Upcast("1", "orders", "Content", """{"steps":[],"b":1,"a":2}"""), second.Upcast("1", "orders", "Content", """{"steps":[],"b":1,"a":2}"""));
    }

    private static string[] Steps(string content) =>
        JsonNode.Parse(content)!["steps"]!.AsArray().Select(step => step!.GetValue<string>()).ToArray();

    private static string Append(string content, string step)
    {
        var node = JsonNode.Parse(content)!.AsObject();
        node["steps"]!.AsArray().Add(step);
        return node.ToJsonString();
    }

    [EfSchemaUpcaster("0", "1")]
    private sealed class ZeroToOne : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => Append(content.Value, "0->1");
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class OneToTwo : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => Append(content.Value, "1->2");
    }

    [EfSchemaUpcaster("2", "3")]
    private sealed class TwoToThree : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => Append(content.Value, "2->3");
    }

    [EfSchemaUpcaster("1", "3")]
    private sealed class OneToThree : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("2", "1")]
    private sealed class TwoToOne : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("2", "2")]
    private sealed class TwoToTwo : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("9", "10")]
    private sealed class NineToTen : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("b", "9")]
    private sealed class BToNine : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    private sealed class Unversioned : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class NotAnUpcaster;

    [EfSchemaUpcaster("1", "2")]
    private sealed class Injected(TimeProvider clock) : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => clock.GetUtcNow().ToString("O");
    }

    [EfSchemaUpcaster("1", "2")]
    private abstract class AbstractUpcaster : IEfSchemaUpcaster
    {
        public abstract string Upcast(EfSchemaContent content);
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class Failing : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => throw new FormatException("not a version-1 document");
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class ReturnsNull : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => null!;
    }
}
