using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T022: each exported workflow definition gets its own file on every platform.</summary>
public sealed class MigratedFileNameTests
{
    [Fact]
    public void A_portable_unique_id_names_its_file()
    {
        Assert.Equal("order-intake.json", MigratedExporter.FileNames(["order-intake"])["order-intake"]);
    }

    [Fact]
    public void Ids_that_differ_only_in_case_get_distinct_files()
    {
        var names = MigratedExporter.FileNames(["OrderIntake", "orderintake"]);

        Assert.NotEqual(names["OrderIntake"].ToLowerInvariant(), names["orderintake"].ToLowerInvariant());
        Assert.StartsWith("OrderIntake-", names["OrderIntake"]);
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a*b?")]
    [InlineData("..")]
    [InlineData("räksmörgås")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("com1")]
    public void A_non_portable_id_gets_a_portable_hashed_name(string id)
    {
        var name = MigratedExporter.FileNames([id])[id];

        Assert.Matches(@"^[A-Za-z0-9._-]+-[0-9a-f]{8}\.json$", name);
        Assert.Equal(name, MigratedExporter.FileNames([id])[id]);
    }
}
