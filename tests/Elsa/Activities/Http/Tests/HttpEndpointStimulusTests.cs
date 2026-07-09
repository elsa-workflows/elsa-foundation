using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for <see cref="HttpEndpointStimulus"/> (spec 089 B): the <c>(template, method)</c> hash identity,
/// template normalization (case- and slash-insensitive, including parameter names), method case-insensitivity and
/// dedupe, and the per-method descriptor set with its routing metadata. The load-bearing property is symmetry:
/// the descriptor a publish emits and the hash a request derives must agree for the same (template, method).
/// </summary>
public sealed class HttpEndpointStimulusTests
{
    [Fact]
    public void Hash_DescribeTime_And_RequestTime_AreSymmetric()
    {
        // Describe-time: the provider hashes each supported method; request-time: the middleware hashes the
        // concrete (template, request-method). They must resolve to the same routing key.
        var describe = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"]));
        var request = HttpEndpointStimulus.Hash("orders/{id}", "GET");

        Assert.Equal(describe.StimulusHash, request);
    }

    [Fact]
    public void Hash_IsPrefixed_Deterministic_AndMethodSensitive()
    {
        Assert.StartsWith("sha256:", HttpEndpointStimulus.Hash("orders/{id}", "GET"));
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "GET"), HttpEndpointStimulus.Hash("orders/{id}", "GET"));

        // Same template, different method → distinct hash.
        Assert.NotEqual(HttpEndpointStimulus.Hash("orders/{id}", "GET"), HttpEndpointStimulus.Hash("orders/{id}", "DELETE"));

        // Different template, same method → distinct hash.
        Assert.NotEqual(HttpEndpointStimulus.Hash("orders/{id}", "GET"), HttpEndpointStimulus.Hash("orders/other", "GET"));
    }

    [Theory]
    [InlineData("orders/{id}", "/Orders/{id}/")]  // surrounding slashes + case
    [InlineData("orders/{id}", "  orders/{id}  ")] // whitespace
    [InlineData("orders/{id}", "orders/{Id}")]     // parameter name case
    public void Hash_IsTemplateNormalized(string canonical, string equivalent)
    {
        Assert.Equal(HttpEndpointStimulus.Hash(canonical, "GET"), HttpEndpointStimulus.Hash(equivalent, "GET"));
    }

    [Fact]
    public void Hash_IsMethodCaseInsensitive()
    {
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "GET"), HttpEndpointStimulus.Hash("orders/{id}", "get"));
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "DELETE"), HttpEndpointStimulus.Hash("orders/{id}", "Delete"));
    }

    [Fact]
    public void Describe_OneDescriptorPerMethod_WithMetadata_AndDistinctHashes()
    {
        var descriptors = HttpEndpointStimulus.Describe("Orders/{Id}", ["GET", "POST", "DELETE"]);

        Assert.Equal(3, descriptors.Count);
        Assert.Equal(3, descriptors.Select(d => d.StimulusHash).Distinct().Count());

        foreach (var descriptor in descriptors)
        {
            Assert.Equal(HttpEndpointRouting.StimulusType, descriptor.StimulusType);
            Assert.Equal("orders/{id}", descriptor.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
            var method = descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey];
            Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", method), descriptor.StimulusHash);
        }
    }

    [Fact]
    public void Describe_DedupesMethodsCaseInsensitively()
    {
        var descriptors = HttpEndpointStimulus.Describe("orders/{id}", ["GET", "get", "Get"]);

        var descriptor = Assert.Single(descriptors);
        Assert.Equal("get", descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey]);
    }

    [Fact]
    public void Describe_OrdersMethodsDeterministically()
    {
        // Authoring order is irrelevant: republish must produce a stable binding set (lowercased ordinal order).
        var one = HttpEndpointStimulus.Describe("orders/{id}", ["POST", "GET", "DELETE"]);
        var two = HttpEndpointStimulus.Describe("orders/{id}", ["DELETE", "POST", "GET"]);

        var methodsOne = one.Select(d => d.Metadata[HttpEndpointRouting.MethodMetadataKey]).ToArray();
        var methodsTwo = two.Select(d => d.Metadata[HttpEndpointRouting.MethodMetadataKey]).ToArray();

        Assert.Equal(new[] { "delete", "get", "post" }, methodsOne);
        Assert.Equal(methodsOne, methodsTwo);
    }

    [Fact]
    public void NormalizeTemplate_Throws_OnNullOrWhitespace()
    {
        Assert.ThrowsAny<ArgumentException>(() => HttpEndpointStimulus.NormalizeTemplate(null!));
        Assert.Throws<ArgumentException>(() => HttpEndpointStimulus.NormalizeTemplate("   "));
    }

    // ---- Issue #592 item 3: normalize literals + parameter names, preserve constraint/default bodies ----

    [Theory]
    [InlineData("Orders/List", "orders/list")]                          // pure literal lowercases wholesale
    [InlineData("Orders/{Id}", "orders/{id}")]                          // literal + parameter name lowercase
    [InlineData("{*Rest}", "{*rest}")]                                  // catch-all parameter name lowercases
    [InlineData("Codes/{Code=ABC}", "codes/{code=ABC}")]               // default value preserved verbatim
    [InlineData("Codes/{Code:regex(^[A-Z]+$)}", "codes/{code:regex(^[A-Z]+$)}")] // constraint body preserved
    [InlineData("Users/{Id?}", "users/{id?}")]                          // optional marker preserved
    public void NormalizeTemplate_LowercasesCaseInsensitiveFacetsOnly(string input, string expected)
    {
        Assert.Equal(expected, HttpEndpointStimulus.NormalizeTemplate(input));
    }

    [Fact]
    public void NormalizeTemplate_DoesNotCorrupt_UppercaseConstraintOrDefault()
    {
        // The corruption under test: lowercasing the whole template would turn {Code=ABC} into {code=abc} and
        // {Code:regex(^[A-Z]+$)} into an all-lowercase regex that no longer matches uppercase codes.
        Assert.Equal("codes/{code=ABC}", HttpEndpointStimulus.NormalizeTemplate("Codes/{Code=ABC}"));
        Assert.Contains("^[A-Z]+$", HttpEndpointStimulus.NormalizeTemplate("Codes/{Code:regex(^[A-Z]+$)}"));
    }

    [Fact]
    public void Describe_StampsOptions_OnEveryMethodDescriptor()
    {
        var options = new HttpEndpointStimulusOptions(
            Authorize: true,
            Policy: "orders-admin",
            RequestTimeout: TimeSpan.FromSeconds(30),
            RequestSizeLimit: 1048576);

        var descriptors = HttpEndpointStimulus.Describe("orders/{id}", ["GET", "POST"], options);

        Assert.Equal(2, descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            Assert.Equal("true", descriptor.Metadata[HttpEndpointRouting.AuthorizeMetadataKey]);
            Assert.Equal("orders-admin", descriptor.Metadata[HttpEndpointRouting.PolicyMetadataKey]);
            Assert.Equal("00:00:30", descriptor.Metadata[HttpEndpointRouting.RequestTimeoutMetadataKey]);
            Assert.Equal("1048576", descriptor.Metadata[HttpEndpointRouting.RequestSizeLimitMetadataKey]);
            // Identity facets remain untouched alongside the options.
            Assert.Equal("orders/{id}", descriptor.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
        }
    }

    [Fact]
    public void Describe_OmitsDefaultOptions_FromMetadata()
    {
        // Absent/default options (no auth, no policy, no timeout, no size limit) contribute no keys, keeping
        // bindings lean and matching the no-options overload byte-for-byte.
        var withNone = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"], HttpEndpointStimulusOptions.None));
        var withoutOptions = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"]));

        foreach (var descriptor in new[] { withNone, withoutOptions })
        {
            Assert.DoesNotContain(HttpEndpointRouting.AuthorizeMetadataKey, descriptor.Metadata.Keys);
            Assert.DoesNotContain(HttpEndpointRouting.PolicyMetadataKey, descriptor.Metadata.Keys);
            Assert.DoesNotContain(HttpEndpointRouting.RequestTimeoutMetadataKey, descriptor.Metadata.Keys);
            Assert.DoesNotContain(HttpEndpointRouting.RequestSizeLimitMetadataKey, descriptor.Metadata.Keys);
        }
    }

    [Fact]
    public void Describe_AuthorizeFalse_OmitsAuthorizeKey_ButKeepsOtherOptions()
    {
        var options = new HttpEndpointStimulusOptions(Authorize: false, Policy: "readers");

        var descriptor = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"], options));

        Assert.DoesNotContain(HttpEndpointRouting.AuthorizeMetadataKey, descriptor.Metadata.Keys);
        Assert.Equal("readers", descriptor.Metadata[HttpEndpointRouting.PolicyMetadataKey]);
    }

    [Fact]
    public void Describe_Options_AreNonIdentity_HashUnchanged()
    {
        // The identity invariant pin: options ride the metadata but never enter the hash, so the routing key is
        // the same with or without them.
        var withOptions = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"], new HttpEndpointStimulusOptions(
            Authorize: true,
            Policy: "orders-admin",
            RequestTimeout: TimeSpan.FromSeconds(30),
            RequestSizeLimit: 1048576)));
        var withoutOptions = Assert.Single(HttpEndpointStimulus.Describe("orders/{id}", ["GET"]));

        Assert.Equal(withoutOptions.StimulusHash, withOptions.StimulusHash);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "GET"), withOptions.StimulusHash);
    }
}
