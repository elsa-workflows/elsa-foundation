using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityDesignFacetTests
{
    [Fact]
    public void Constructor_RequiresKind()
    {
        var payload = Payload();

        Assert.Throws<ArgumentException>(() => new ActivityDesignFacet("", "1.0.0", payload));
    }

    [Fact]
    public void Constructor_RequiresSchemaVersion()
    {
        var payload = Payload();

        Assert.Throws<ArgumentException>(() => new ActivityDesignFacet("elsa.test.shape", "", payload));
    }

    [Fact]
    public void Constructor_RequiresPayload()
    {
        Assert.Throws<ArgumentException>(() => new ActivityDesignFacet("elsa.test.shape", "1.0.0", default));
    }

    [Fact]
    public void Constructor_ClonesPayload()
    {
        ActivityDesignFacet facet;
        using (var document = JsonDocument.Parse("""{ "value": 42 }"""))
            facet = new ActivityDesignFacet("elsa.test.shape", "1.0.0", document.RootElement);

        Assert.Equal(42, facet.Payload.GetProperty("value").GetInt32());
    }

    private static JsonElement Payload()
    {
        using var document = JsonDocument.Parse("""{ "value": true }""");
        return document.RootElement.Clone();
    }
}
