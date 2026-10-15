using System.Text.Json;
using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests;

public sealed class SensitiveValueTests
{
    [Fact]
    public void ToString_is_always_redacted()
    {
        var value = new SensitiveValue("super-secret");
        Assert.Equal(SensitiveValue.Redacted, value.ToString());
        Assert.Equal("super-secret", value.Value);
    }

    [Fact]
    public void Json_serialization_throws()
    {
        var value = new SensitiveValue("super-secret");
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(value));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<SensitiveValue>("\"x\""));
    }
}
