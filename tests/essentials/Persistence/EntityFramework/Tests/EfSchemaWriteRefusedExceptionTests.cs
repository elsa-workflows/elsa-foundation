using Elsa.Primitives.Exceptions;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Spec 180, FR-016a: the write refusal is its own type, with a stable code, the family and both versions, and it can never
/// be caught by the filters that turn an exception into corruption or a 400.
/// </summary>
public sealed class EfSchemaWriteRefusedExceptionTests
{
    private static readonly EfSchemaWriteRefusedException Refusal = new("Orders", "1", "2");

    /// <remarks>
    /// <c>InvalidDataException</c> is sealed, so no subtype of it can exist and an assertion about it could never fail;
    /// the types below are the derivable ones a refactor might reach for.
    /// </remarks>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(System.Text.Json.JsonException))]
    public void The_refusal_is_not_assignable_to_any_type_the_catch_filters_and_fault_ladders_name(Type caught) =>
        Assert.False(caught.IsInstanceOfType(Refusal), $"EfSchemaWriteRefusedException must not be catchable as {caught.Name}.");

    [Fact]
    public void The_refusal_carries_a_stable_code_the_family_and_both_versions()
    {
        Assert.Equal("schema-write-refused", Refusal.Code);
        Assert.Equal("Orders", Refusal.Family);
        Assert.Equal("1", Refusal.WriteVersion);
        Assert.Equal("2", Refusal.RequiredVersion);
        Assert.Contains("'Orders'", Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'1'", Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'2'", Refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An API resolves no EF Core, so it answers the refusal as the persistence-agnostic base both EF refusals derive
    /// from; a refusal that did not derive from it would reach every API's default arm, a 500.
    /// </summary>
    [Fact]
    public void Both_refusals_are_the_base_every_domain_api_answers()
    {
        SchemaWriteRefusedException[] refusals =
        [
            Refusal,
            new SchemaFinalization.EfSchemaFamilyWritesRefusedException("Orders", "1", "3", ["1", "2"])
        ];

        Assert.All(refusals, refusal => Assert.Equal(SchemaWriteRefusedException.RefusalCode, refusal.Code));
        Assert.Equal("schema-write-refused", SchemaWriteRefusedException.RefusalCode);
    }

    /// <summary>Spec 182 extends the refusal with a feature and a reason; the extension keeps the code and the versions.</summary>
    [Fact]
    public void A_derived_refusal_states_its_own_reason_and_keeps_the_code()
    {
        var dormant = new DormantFeatureRefusal();

        Assert.Equal(EfSchemaWriteRefusedException.RefusalCode, dormant.Code);
        Assert.Equal("waiting for finalization", dormant.Message);
        Assert.Equal("2", dormant.RequiredVersion);
    }

    private sealed class DormantFeatureRefusal() : EfSchemaWriteRefusedException("Orders", "1", "2", "waiting for finalization");
}
