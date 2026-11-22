using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit
{
    public sealed class JsonModuleMetadataTests
    {
        [Fact]
        public void Generated_metadata_does_not_claim_foreign_shared_or_BCL_types()
        {
            const string owner = "Elsa.Serialization.Tests.OwnerA";
            var context = Context(); var host = JsonBaseline.CreateCopy();
            Assert.NotNull(JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, owner));
            Assert.NotNull(JsonModuleMetadata.Resolve(context, typeof(List<OwnerA.Dto>), host, owner));
            Assert.Null(JsonModuleMetadata.Resolve(context, typeof(OwnerB.Dto), host, owner));
            Assert.Null(JsonModuleMetadata.Resolve(context, typeof(string), host, owner));
            Assert.False(JsonModuleMetadata.Owns(typeof(Dictionary<OwnerA.Dto, OwnerB.Dto>), owner));
        }

        [Fact]
        public void Owner_converters_are_scoped_to_owned_metadata_without_mutating_host_options()
        {
            var host = JsonBaseline.CreateCopy(); var context = Context();
            var initial = host.Converters.ToArray();
            var info = JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, "Elsa.Serialization.Tests.OwnerA")!;
            Assert.Same(host, info.Options);
            var element = JsonSerializer.SerializeToElement(new OwnerA.Dto(OwnerA.Status.InProgress), info);
            Assert.Equal("inProgress", element.GetProperty("status").GetString());
            Assert.Equal(initial, host.Converters);
            Assert.False(host.PropertyNameCaseInsensitive); Assert.Null(host.DictionaryKeyPolicy);
        }

        [Fact]
        public void Two_generated_resolvers_have_disjoint_ownership_even_when_both_contexts_describe_both_types()
        {
            var context = Context(); var host = JsonBaseline.CreateCopy();
            foreach (var type in new[] { typeof(OwnerA.Dto), typeof(OwnerB.Dto), typeof(List<OwnerA.Dto>), typeof(string) })
            {
                var claims = new[] { "Elsa.Serialization.Tests.OwnerA", "Elsa.Serialization.Tests.OwnerB" }
                    .Count(owner => JsonModuleMetadata.Resolve(context, type, host, owner) is not null);
                Assert.InRange(claims, 0, 1);
            }
            // Negative controls: a foreign resolver and a duplicate owner are detected independently.
            Assert.NotNull(((IJsonTypeInfoResolver)context).GetTypeInfo(typeof(OwnerB.Dto), host));
            Assert.False(JsonModuleMetadata.Owns(typeof(OwnerB.Dto), "Elsa.Serialization.Tests.OwnerA"));
            Assert.Equal(2, new[] { "Elsa.Serialization.Tests.OwnerA", "Elsa.Serialization.Tests.OwnerA" }
                .Count(owner => JsonModuleMetadata.Resolve(context, typeof(OwnerA.Dto), host, owner) is not null));
        }

        private static OwnedContext Context()
        {
            var options = JsonBaseline.CreateCopy(); options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return new OwnedContext(options);
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(OwnerA.Dto))]
    [JsonSerializable(typeof(OwnerB.Dto))]
    [JsonSerializable(typeof(List<OwnerA.Dto>))]
    [JsonSerializable(typeof(string))]
    internal partial class OwnedContext : JsonSerializerContext;
}
namespace Elsa.Serialization.Tests.OwnerA
{
    public enum Status { InProgress }
    public sealed record Dto(Status Status);
}
namespace Elsa.Serialization.Tests.OwnerB
{
    public sealed record Dto(string Name);
}
