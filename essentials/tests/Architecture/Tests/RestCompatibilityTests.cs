using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Canary fixture proving an authoring-only replacement has no observable delta.</summary>
public sealed class RestCompatibilityTests
{
    [Fact]
    public void Equivalent_before_and_after_authoring_uses_the_shared_gate_without_unrelated_registry_entries()
    {
        var endpoint = new EndpointIdentity("/api/canary/{id}", "GET");
        var before = new CompatibilityEvidenceSet { Http = [Observation(endpoint)] };
        var after = new CompatibilityEvidenceSet { Http = [Observation(endpoint)] };
        var result = CompatibilityComparer.Compare(before, after);

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    private static HttpCompatibilityObservation Observation(EndpointIdentity endpoint) => new()
    {
        Endpoint = endpoint,
        Case = "default",
        Binding = "route=id",
        Json = "{\"id\":7}",
        StatusCode = 200,
        ContentType = "application/json",
        Body = "{\"id\":7}",
        Headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-type"] = "application/json"
        }
    };
}
