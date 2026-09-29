using Elsa.Primitives.Exceptions;
using Xunit;

namespace Elsa.Primitives.Tests.Exceptions;

/// <summary>
/// Spec 182, FR-013 and SC-006: the dormancy refusal is spec 180's write refusal, keeping its type and versions, but
/// carrying its own stable code (Q17), plus the feature and a caller-neutral reason, and no filter that turns an
/// exception into corruption or a 400 catches it.
/// </summary>
public sealed class SchemaDormancyRefusedExceptionTests
{
    private const string Reason = "It becomes available once every host can read version '2' of schema family 'Orders'.";
    private static readonly SchemaDormancyRefusedException Refusal = new("Orders", "1", "2", "OrdersApi", Reason);

    /// <remarks>
    /// <c>InvalidDataException</c> is sealed, so no subtype of it can exist; it is listed because spec 182's FR-013 names
    /// all six, and the assertion still pins that nobody unseals it and derives the refusal from it.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(System.Text.Json.JsonException))]
    [InlineData(typeof(InvalidDataException))]
    public void The_refusal_is_not_assignable_to_any_type_the_catch_filters_and_fault_ladders_name(Type caught) =>
        Assert.False(caught.IsInstanceOfType(Refusal), $"SchemaDormancyRefusedException must not be catchable as {caught.Name}.");

    [Fact]
    public void The_refusal_is_the_write_refusal_every_domain_api_answers_with_its_own_code_family_and_versions()
    {
        SchemaWriteRefusedException refusal = Refusal;

        Assert.Equal(SchemaDormancyRefusedException.RefusalCode, refusal.Code);
        Assert.NotEqual(SchemaWriteRefusedException.RefusalCode, refusal.Code);
        Assert.Equal(("Orders", "1", "2"), (refusal.Family, refusal.WriteVersion, refusal.RequiredVersion));
    }

    [Fact]
    public void The_refusal_codes_are_the_literal_stable_codes_the_spec_names()
    {
        Assert.Equal("schema-version-not-finalized", SchemaDormancyRefusedException.RefusalCode);
        Assert.Equal("schema-write-refused", SchemaWriteRefusedException.RefusalCode);
        Assert.Equal("schema-version-not-finalized", Refusal.Code);
    }

    [Fact]
    public void The_refusal_names_the_feature_and_the_reason_in_its_properties_and_its_message()
    {
        Assert.Equal("OrdersApi", Refusal.FeatureId);
        Assert.Equal(Reason, Refusal.Reason);
        foreach (var part in new[] { "'OrdersApi'", "'Orders'", "'1'", "'2'", Reason, "nothing it carried was saved" })
            Assert.Contains(part, Refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_refusal_raised_for_an_operation_that_named_no_feature_says_so(string? feature)
    {
        var refusal = new SchemaDormancyRefusedException("Orders", "1", "2", feature, Reason);

        Assert.Null(refusal.FeatureId);
        Assert.StartsWith("This operation is dormant", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_without_a_reason_is_refused() =>
        Assert.Throws<ArgumentException>(() => new SchemaDormancyRefusedException("Orders", "1", "2", "OrdersApi", " "));
}
