using System.Text.Json;
using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class SensitiveValueTests
{
    [Fact]
    public void ToString_IsRedacted_AndJsonThrows()
    {
        var value = new SensitiveValue("super-secret");
        Assert.Equal(SensitiveValue.Redacted, value.ToString());
        Assert.Equal("super-secret", value.Reveal());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(value));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<SensitiveValue>("\"x\""));
    }

    [Fact]
    public void AccessToken_ToString_IsRedacted()
    {
        var token = new AccessToken("Bearer", "tok-123", DateTimeOffset.UtcNow);
        Assert.Equal(SensitiveValue.Redacted, token.ToString());
    }
}
