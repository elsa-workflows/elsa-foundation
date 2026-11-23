using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class JsonHashInputVectorTests
{
    [Fact]
    public void Core_and_the_sdk_share_frozen_independently_computed_canonical_vectors()
    {
        using var resource = typeof(JsonHashInputVectorTests).Assembly.GetManifestResourceStream("JsonHashInput.Vectors.json")!;
        var vectors = JsonSerializer.Deserialize<List<Vector>>(resource, JsonBaseline.Options)!;
        Assert.Equal(15, vectors.Count);
        foreach (var vector in vectors)
        {
            using var json = JsonDocument.Parse(vector.Json, new JsonDocumentOptions { MaxDepth = 1000 });
            var canonical = Encoding.UTF8.GetString(JsonHashInput.CanonicalBytes(json.RootElement));
            var hash = JsonHashInput.Hash(json.RootElement);
            Assert.True(canonical == vector.Canonical && hash == vector.Sha256,
                $"First-stable hash freeze: changes require an explicit identity/migration decision. Vector {vector.Name}; canonical {canonical}; hash {hash}.");
        }
    }

    private sealed record Vector(string Name, string Json, string Canonical, string Sha256);
}
