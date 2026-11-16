using System.Text.Json;
using Elsa.Secrets.Core.Models;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretReferenceSerializationTests
{
    [Fact]
    public void Secret_Reference_Serializes_Without_Raw_Value()
    {
        var reference = new SecretReference("payments.api", SecretTypeNames.Text);

        var json = JsonSerializer.Serialize(reference, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("payments.api", json);
        Assert.Contains("text", json);
        Assert.DoesNotContain("secret-value", json);
    }
}
