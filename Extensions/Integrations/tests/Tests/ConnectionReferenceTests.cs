using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests;

public sealed class ConnectionReferenceTests
{
    [Fact]
    public void Trims_name_and_optional_type()
    {
        var reference = new ConnectionReference("  demo  ", "  http  ");
        Assert.Equal("demo", reference.Name);
        Assert.Equal("http", reference.TypeName);
        Assert.Equal("demo (http)", reference.ToString());
    }

    [Fact]
    public void Rejects_a_blank_name()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionReference(" "));
    }
}
