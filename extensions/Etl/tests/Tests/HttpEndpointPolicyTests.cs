using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Tests;

public sealed class HttpEndpointPolicyTests
{
    [Theory]
    [InlineData("https://other.example/data")]
    [InlineData("http://source.example/data")]
    [InlineData("https://source.example:444/data")]
    [InlineData("https://user@source.example/data")]
    [InlineData("https://source.example/data#fragment")]
    [InlineData("https://source.example/other")]
    [InlineData("https://source.example/data/child")]
    [InlineData("https://source.example/data/../other")]
    [InlineData("https://source.example/data/%2e%2e/other")]
    [InlineData("https://source.example/data%2fchild")]
    [InlineData("https://source.example/data%5cchild")]
    [InlineData("https://source.example/data%252fchild")]
    [InlineData("https://source.example/data%25%32%66child")]
    [InlineData("https://source.example/data%0a")]
    public void Refuses_paths_and_origins_outside_exact_host_policy(string request)
    {
        var policy = Create();
        var error = Assert.Throws<EtlBindingException>(() => policy.Validate(new Uri(request)));
        Assert.Equal("etl.http-endpoint-unavailable", error.Code);
        Assert.DoesNotContain(request, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?cursor=opaque%2Fvalue", "https://source.example/data?cursor=opaque%2Fvalue")]
    [InlineData("./data?cursor=2", "https://source.example/data?cursor=2")]
    [InlineData("https://SOURCE.example:443/ignored/../data", "https://source.example/data")]
    public void Allows_canonical_exact_paths_and_query_only_continuations(string value, string expected)
    {
        var policy = Create();
        Assert.Equal(new Uri(expected), policy.Resolve(value, policy.Endpoint));
    }

    [Theory]
    [InlineData("https://source.example/data\\child")]
    [InlineData(" https://source.example/data")]
    [InlineData("https://source.example/data\n")]
    [InlineData("//other.example/data")]
    [InlineData("../other")]
    public void Relative_resolution_does_not_discard_policy_relevant_syntax(string value)
    {
        var policy = Create();
        Assert.Throws<EtlBindingException>(() => policy.Resolve(value, policy.Endpoint));
    }

    [Fact]
    public void Exact_allowlist_is_snapshotted_and_http_requires_explicit_host_opt_in()
    {
        var paths = new List<string> { "/data", "/next" };
        var policy = new HttpEndpointPolicy("http://source.example/data", paths, true);
        paths.Add("/unapproved");
        policy.Validate(new Uri("http://source.example/next"));
        Assert.Throws<EtlBindingException>(() => policy.Validate(new Uri("http://source.example/unapproved")));
        Assert.Throws<EtlBindingException>(() => new HttpEndpointPolicy("http://source.example/data", [], false));
        Assert.DoesNotContain("source.example", policy.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://other.example/data")]
    [InlineData("//other.example/data")]
    [InlineData("/data?query=1")]
    [InlineData("/data#fragment")]
    [InlineData("/data%2fchild")]
    public void Host_path_entries_cannot_change_origin_or_embed_query_policy(string path) =>
        Assert.Throws<EtlBindingException>(() => new HttpEndpointPolicy("https://source.example/data", [path], false));

    [Fact]
    public void Explicit_literal_percent_path_is_allowed_without_a_second_escape_sequence()
    {
        var policy = new HttpEndpointPolicy("https://source.example/data/100%25/items", [], false);
        policy.Validate(policy.Endpoint);
        Assert.Equal(new Uri("https://source.example/data/100%25/items?cursor=next"), policy.Resolve("?cursor=next", policy.Endpoint));
    }

    private static HttpEndpointPolicy Create() => new("https://source.example/data", [], false);
}
