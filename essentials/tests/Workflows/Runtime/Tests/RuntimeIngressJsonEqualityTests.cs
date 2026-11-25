using System.Text.Json;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeIngressJsonEqualityTests
{
    [Theory]
    [InlineData("1e2147483648", "10e2147483647")]
    [InlineData("{\"OwnerKey\":1e2147483648}", "{\"OwnerKey\":10e2147483647}")]
    [InlineData("{\"name\":\"Ren\\u00e9\",\"n\":1.0}", "{\"n\":1e0,\"name\":\"René\"}")]
    public void Admission_comparisons_accept_exact_equivalent_JSON_without_bounded_exponent_parsing(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        Assert.True(RuntimeIngressAdmissionValidation.Same(a.RootElement, b.RootElement));
    }

    [Theory]
    [InlineData("{}", "{\"x\":null}")]
    [InlineData("9007199254740993", "9007199254740992")]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("{\"OwnerKey\":1}", "{\"ownerKey\":1}")]
    public void Admission_comparisons_retain_presence_precision_order_and_key_identity(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        Assert.False(RuntimeIngressAdmissionValidation.Same(a.RootElement, b.RootElement));
    }
}
