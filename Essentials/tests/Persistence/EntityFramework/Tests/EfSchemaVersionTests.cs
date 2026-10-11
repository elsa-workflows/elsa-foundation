using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// ADR 0077 separates two conditions that were reported identically: a corrupt row, and a row written by a build this
/// host does not run. They call for different operator responses, so the point of these tests is that the distinction
/// cannot silently collapse back into one. Spec 180's FR-007 widens "a version this build reads" from one version to a
/// family's readable set, and keeps every other part of the rule.
/// </summary>
public sealed class EfSchemaVersionTests
{
    private static readonly EfSchemaChain RuntimeArtifact = SchemaChains.Single("RuntimeArtifact", "1.0.0");
    private static readonly EfSchemaChain Orders = SchemaChains.Declare("Orders", "3", SchemaChains.Step<OneToTwo>(), SchemaChains.Step<TwoToThree>());

    [Fact]
    public void Skew_does_not_surface_as_the_corruption_type()
    {
        var exception = Record.Exception(() => EfSchemaVersion.EnsureReadable(RuntimeArtifact, "2.0.0"));

        Assert.IsType<EfSchemaVersionSkewException>(exception);
    }

    /// <summary>
    /// The EF stores wrap their read paths in type-filtered catch blocks. Skew must not be assignable to
    /// any type those filters name, or the store would catch it and re-report it as corruption or as a
    /// provider failure — the exact behavior ADR 0077 removes.
    /// </summary>
    /// <remarks>
    /// <c>InvalidDataException</c> is deliberately absent from this list: it is sealed, so an assertion
    /// about it could never fail and would only look like protection. The types below are the ones a
    /// plausible refactor might actually reach for, and each is genuinely derivable.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(System.Text.Json.JsonException))]
    public void Skew_is_not_assignable_to_any_type_the_store_catch_filters_name(Type caught)
    {
        var exception = Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.EnsureReadable(RuntimeArtifact, "2.0.0"));

        Assert.False(
            caught.IsInstanceOfType(exception),
            $"EfSchemaVersionSkewException must not be catchable as {caught.Name}: the EF stores filter on "
            + "that type and would swallow version skew.");
    }

    /// <summary>
    /// FR-007: the diagnostic names the family, the version found and the readable set, and its remedy describes the
    /// gate rather than saying the versions cannot run alongside each other.
    /// </summary>
    [Fact]
    public void Skew_diagnostic_names_the_family_the_version_found_and_the_readable_set()
    {
        var exception = Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.EnsureReadable(Orders, "4"));

        Assert.Equal("Orders", exception.Family);
        Assert.Equal("4", exception.Found);
        Assert.Equal("3", exception.Expected);
        Assert.Equal(["1", "2", "3"], exception.ReadableVersions);

        Assert.Contains("'Orders'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'4'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'1', '2', '3'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("finalized", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("alongside", exception.Message, StringComparison.Ordinal);

        // An operator reading this must not be sent looking for data damage.
        Assert.DoesNotContain("corrupt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_row_carrying_no_version_is_skew_rather_than_a_null_reference()
    {
        var exception = Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.EnsureReadable(RuntimeArtifact, null));

        Assert.Null(exception.Found);
        Assert.Contains("(none)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_version_in_the_readable_set_passes_every_guard()
    {
        foreach (var version in (string[])["1", "2", "3"])
        {
            EfSchemaVersion.EnsureReadable(Orders, version);
            Assert.True(EfSchemaVersion.IsReadable(Orders, version));
            Assert.True(EfSchemaVersion.Readable(Orders, version));
            Assert.False(EfSchemaVersion.NotReadable(Orders, version));
        }
    }

    /// <summary>
    /// The drop-in guards keep the polarity of the clause they replaced, so a surrounding integrity
    /// condition keeps its meaning. Getting this backwards would invert an integrity check rather than
    /// fail loudly, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void The_drop_in_guards_keep_their_clause_polarity_and_throw_on_skew()
    {
        Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.Readable(RuntimeArtifact, "2.0.0"));
        Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.NotReadable(RuntimeArtifact, "2.0.0"));
        Assert.False(EfSchemaVersion.IsReadable(RuntimeArtifact, "2.0.0"));
    }

    [Fact]
    public void Version_comparison_is_ordinal()
    {
        Assert.False(EfSchemaVersion.IsReadable(RuntimeArtifact, "1.0.0 "));
        Assert.False(EfSchemaVersion.IsReadable(RuntimeArtifact, "1.0.O"));
    }

    /// <summary>
    /// A read path that cannot apply the chain accepts the current version alone, and reports skew naming only that
    /// version, even for a version the family's chain reaches.
    /// </summary>
    [Fact]
    public void A_read_path_that_cannot_upcast_accepts_the_current_version_alone()
    {
        EfSchemaVersion.EnsureCurrent(Orders, "3");

        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => EfSchemaVersion.EnsureCurrent(Orders, "2"));

        Assert.Equal("2", skew.Found);
        Assert.Equal(["3"], skew.ReadableVersions);
    }

    [EfSchemaUpcaster("1", "2")]
    private sealed class OneToTwo : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }

    [EfSchemaUpcaster("2", "3")]
    private sealed class TwoToThree : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
    }
}
