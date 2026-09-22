using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// ADR 0077 separates two conditions that were reported identically: a corrupt row envelope, and a row
/// written by a module version this build does not run. They call for different operator responses, so
/// the point of these tests is that the distinction cannot silently collapse back into one.
/// </summary>
public sealed class EfSchemaVersionTests
{
    [Fact]
    public void Skew_does_not_surface_as_the_corruption_type()
    {
        var exception = Record.Exception(() => EfSchemaVersion.EnsureReadable("RuntimeOperationalState", "2.0.0", "1.0.0"));

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
        var exception = Assert.Throws<EfSchemaVersionSkewException>(
            () => EfSchemaVersion.EnsureReadable("RuntimeArtifact", "2.0.0", "1.0.0"));

        Assert.False(
            caught.IsInstanceOfType(exception),
            $"EfSchemaVersionSkewException must not be catchable as {caught.Name}: the EF stores filter on "
            + "that type and would swallow version skew.");
    }

    [Fact]
    public void Skew_diagnostic_names_the_module_and_both_versions()
    {
        var exception = Assert.Throws<EfSchemaVersionSkewException>(
            () => EfSchemaVersion.EnsureReadable("BookmarkState", "3.1.0", "1.0.0"));

        Assert.Equal("BookmarkState", exception.Module);
        Assert.Equal("3.1.0", exception.Found);
        Assert.Equal("1.0.0", exception.Expected);

        Assert.Contains("BookmarkState", exception.Message, StringComparison.Ordinal);
        Assert.Contains("3.1.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", exception.Message, StringComparison.Ordinal);

        // An operator reading this must not be sent looking for data damage.
        Assert.DoesNotContain("corrupt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_row_carrying_no_version_is_skew_rather_than_a_null_reference()
    {
        var exception = Assert.Throws<EfSchemaVersionSkewException>(
            () => EfSchemaVersion.EnsureReadable("RuntimeArtifact", null, "1.0.0"));

        Assert.Null(exception.Found);
        Assert.Contains("(none)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_matching_version_passes_every_guard()
    {
        EfSchemaVersion.EnsureReadable("RuntimeArtifact", "1.0.0", "1.0.0");

        Assert.True(EfSchemaVersion.IsReadable("1.0.0", "1.0.0"));
        Assert.True(EfSchemaVersion.Readable("RuntimeArtifact", "1.0.0", "1.0.0"));
        Assert.False(EfSchemaVersion.NotReadable("RuntimeArtifact", "1.0.0", "1.0.0"));
    }

    /// <summary>
    /// The drop-in guards keep the polarity of the clause they replaced, so a surrounding envelope
    /// condition keeps its meaning. Getting this backwards would invert an envelope check rather than
    /// fail loudly, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void The_drop_in_guards_keep_their_clause_polarity_and_throw_on_skew()
    {
        Assert.Throws<EfSchemaVersionSkewException>(
            () => EfSchemaVersion.Readable("RuntimeArtifact", "2.0.0", "1.0.0"));

        Assert.Throws<EfSchemaVersionSkewException>(
            () => EfSchemaVersion.NotReadable("RuntimeArtifact", "2.0.0", "1.0.0"));
    }

    [Fact]
    public void Version_comparison_is_ordinal()
    {
        Assert.False(EfSchemaVersion.IsReadable("1.0.0 ", "1.0.0"));
        Assert.False(EfSchemaVersion.IsReadable("1.0.O", "1.0.0"));
    }
}
