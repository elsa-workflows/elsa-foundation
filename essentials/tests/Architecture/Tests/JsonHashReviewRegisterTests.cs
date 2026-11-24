using System.Reflection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashReviewRegisterTests
{
    [Fact]
    public void A_review_accounts_for_exact_sites_and_never_grants_a_helper_a_future_hash_allowance()
    {
        var graph = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) => System.Security.Cryptography.SHA256.HashData(bytes);
            }
            """))]);
        var group = Assert.Single(graph.References.Where(reference => reference.Role != "ordinary").GroupBy(reference => reference.Owner));
        var row = new JsonHashReviewRegister.Owner(group.Key, "non_json", "Opaque physical bytes, never serialized JSON.", ["Recipe.cs"], [], JsonHashReviewRegister.Sites(group));
        var reviewed = new JsonHashReviewRegister.Register(1, "consumer", "authority", [row]);
        Assert.Empty(JsonHashReviewRegister.Validate(graph, reviewed, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Sites = [] }] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Classification = "shared_authority" }] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Reason = "" }] }, new HashSet<string>()));
        var changed = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) {
                    _ = System.Security.Cryptography.SHA256.HashData(bytes);
                    return System.Security.Cryptography.SHA256.HashData(bytes);
                }
            }
            """))]);
        Assert.NotEmpty(JsonHashReviewRegister.Validate(changed, reviewed, new HashSet<string>()));
        var added = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) => System.Security.Cryptography.SHA256.HashData(bytes);
                public static byte[] NewJsonHash() => System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Value = 1 }));
            }
            """))]);
        Assert.NotEmpty(JsonHashReviewRegister.Validate(added, reviewed, new HashSet<string>()));
    }
}
