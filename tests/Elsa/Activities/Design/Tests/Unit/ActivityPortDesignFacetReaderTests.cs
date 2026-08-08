using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Branch coverage for the single reader of the <c>"ports"</c> facet convention (§2.23.2). It is the one
/// place the convention is parsed: the served authoring catalog and the build-time contract fragments both
/// call it, so a change here moves both surfaces together (one-projection rule, spec 149 / RFC #1191).
/// </summary>
public sealed class ActivityPortDesignFacetReaderTests
{
    private static ActivityDesignFacet Facet(object payload) =>
        new("elsa.outcomes", "1", JsonSerializer.SerializeToElement(payload));

    [Fact]
    public void Facet_without_a_ports_property_yields_nothing()
    {
        Assert.Empty(ActivityPortDesignFacetReader.Read(Facet(new { mode = "sequence" })));
    }

    [Fact]
    public void Ports_that_are_not_an_array_yield_nothing()
    {
        Assert.Empty(ActivityPortDesignFacetReader.Read(Facet(new { ports = "Done" })));
    }

    [Fact]
    public void Non_object_and_unnamed_entries_are_skipped()
    {
        var facet = Facet(new
        {
            ports = new object?[]
            {
                "Done",
                42,
                new { type = "outcome" },
                new { name = "   " },
                new { name = "Kept", type = "outcome" }
            }
        });

        var port = Assert.Single(ActivityPortDesignFacetReader.Read(facet));

        Assert.Equal("Kept", port.Name);
    }

    [Fact]
    public void Missing_optional_members_fall_back_to_the_convention_defaults()
    {
        var facet = Facet(new { ports = new[] { new { name = "Done" } } });

        var port = Assert.Single(ActivityPortDesignFacetReader.Read(facet));

        Assert.Equal("Done", port.Name);
        Assert.Equal("Done", port.DisplayName);
        Assert.Equal("Done", port.ReferenceKey);
        Assert.Null(port.Type);
        Assert.True(port.IsBrowsable);
    }

    [Fact]
    public void Explicit_members_are_honored()
    {
        var facet = Facet(new
        {
            ports = new[]
            {
                new { name = "Approved", displayName = "Approved by reviewer", type = "outcome", isBrowsable = false, referenceKey = "approved" }
            }
        });

        var port = Assert.Single(ActivityPortDesignFacetReader.Read(facet));

        Assert.Equal("Approved", port.Name);
        Assert.Equal("Approved by reviewer", port.DisplayName);
        Assert.Equal("outcome", port.Type);
        Assert.Equal("approved", port.ReferenceKey);
        Assert.False(port.IsBrowsable);
    }

    [Fact]
    public void Wrong_typed_optional_members_fall_back_instead_of_throwing()
    {
        var facet = Facet(new { ports = new[] { new { name = "Done", displayName = 7, isBrowsable = "yes" } } });

        var port = Assert.Single(ActivityPortDesignFacetReader.Read(facet));

        Assert.Equal("Done", port.DisplayName);
        Assert.True(port.IsBrowsable);
    }

    [Fact]
    public void ReadAll_preserves_facet_and_port_order()
    {
        var facets = new[]
        {
            Facet(new { ports = new[] { new { name = "Done" }, new { name = "Failed" } } }),
            Facet(new { ports = new[] { new { name = "Timeout" } } })
        };

        Assert.Equal(
            ["Done", "Failed", "Timeout"],
            ActivityPortDesignFacetReader.ReadAll(facets).Select(port => port.Name));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ActivityPortDesignFacetReader.Read(null!));
        Assert.Throws<ArgumentNullException>(() => ActivityPortDesignFacetReader.ReadAll(null!).ToArray());
    }
}
