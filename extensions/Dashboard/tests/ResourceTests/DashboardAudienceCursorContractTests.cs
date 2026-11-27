using System.Reflection;
using System.Text;
using Elsa.Studio.Dashboard.Api;
using Elsa.Studio.Dashboard.Core.Resources;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardAudienceCursorContractTests
{
    private static readonly Type Owner = typeof(DashboardResourcesApiFeature).Assembly
        .GetType("Elsa.Studio.Dashboard.Api.DashboardAudienceCursors", throwOnError: true)!;
    private static object Create() => Activator.CreateInstance(Owner, nonPublic: true)!;
    private static string Write(object owner, string scope, int skip) =>
        (string)Owner.GetMethod("Write")!.Invoke(owner, [scope, skip])!;
    private static int Read(object owner, string token, string scope) =>
        (int)Owner.GetMethod("Read")!.Invoke(owner, [token, scope])!;

    [Fact]
    public void Canonical_payload_spelling_keeps_the_signature_and_scope_binding()
    {
        var owner = Create();
        var token = Write(owner, "scope-é", 7);
        var parts = token.Split('.');
        Assert.Equal("{\"scope\":\"scope-é\",\"skip\":7}", Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])));
        var variant = " { \"skip\" : 7, \"scope\" : \"scope-\\u00e9\" } ";
        Assert.Equal(7, Read(owner, Convert.ToBase64String(Encoding.UTF8.GetBytes(variant)) + "." + parts[1], "scope-é"));
        var error = Assert.Throws<TargetInvocationException>(() => Read(owner, token, "different-scope"));
        Assert.Equal("validation", Assert.IsType<DashboardResourceException>(error.InnerException).Code);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"scope\":\"\\uD800\",\"skip\":0}")]
    [InlineData("{\"\\uD800\":0,\"scope\":\"scope\",\"skip\":0}")]
    [InlineData("{\"scope\":\"scope\",\"skip\":null}")]
    public void Malformed_cursor_payloads_use_the_owned_refusal(string payload)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) + "." + Convert.ToBase64String(new byte[32]);
        var error = Assert.Throws<TargetInvocationException>(() => Read(Create(), token, "scope"));
        Assert.Equal("validation", Assert.IsType<DashboardResourceException>(error.InnerException).Code);
    }
}
