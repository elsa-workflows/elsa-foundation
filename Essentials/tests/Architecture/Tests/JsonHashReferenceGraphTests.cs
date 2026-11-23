using System.Reflection;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashReferenceGraphTests
{
    private const string Source = """
        using System;
        using System.Text;
        using System.Security.Cryptography;
        using Elsa.Serialization.Core;
        using Digest = System.Security.Cryptography.SHA256;
        using static System.Security.Cryptography.SHA256;
        namespace HashFixture {
        public interface IDigest { byte[] Compute(byte[] value); }
        public class ActualDigest : IDigest { public byte[] Compute(byte[] value) => SHA256.HashData(value); }
        public abstract class DigestBase { public abstract byte[] Compute(byte[] value); }
        public class DerivedDigest : DigestBase { public override byte[] Compute(byte[] value) => SHA256.HashData(value); }
        public class InjectedRecipe
        {
            private readonly Func<byte[], byte[]> algorithm;
            public InjectedRecipe(Func<byte[], byte[]> algorithm) { this.algorithm = algorithm; }
            public byte[] Run(byte[] value) => algorithm(value);
        }
        public static class Recipes
        {
            private static readonly Func<byte[], byte[]> Algorithm = SHA256.HashData;
            private static Func<byte[], byte[]> AlgorithmProperty => Algorithm;
            public static byte[] Alias(byte[] value) => Digest.HashData(value);
            public static byte[] StaticImport(byte[] value) => HashData(value);
            public static byte[] Twice(byte[] value) { _ = SHA256.HashData(value); return SHA256.HashData(value); }
            public static byte[] Field(byte[] value) => Algorithm(value);
            public static byte[] Property(byte[] value) => AlgorithmProperty(value);
            public static byte[] Dispatch(IDigest algorithm, byte[] value) => algorithm.Compute(value);
            public static byte[] VirtualDispatch(DigestBase algorithm, byte[] value) => algorithm.Compute(value);
            public static InjectedRecipe Factory() => new(SHA256.HashData);
            public static string Gateway(System.Text.Json.JsonElement value) => JsonHashInput.Hash(value);
            public static string GenericGateway(string value) => JsonHashInput.Hash<string>(value);
            public static byte[] Mixed(System.Text.Json.JsonElement canonical, string rawJson)
            {
                var first = JsonHashInput.CanonicalBytes(canonical);
                var second = Encoding.UTF8.GetBytes(rawJson);
                var frame = new byte[first.Length + second.Length];
                first.CopyTo(frame, 0); second.CopyTo(frame, first.Length);
                return SHA256.HashData(frame);
            }
            public static byte[] PhysicalHelper(string opaque) => SHA256.HashData(Encoding.UTF8.GetBytes(opaque));
            public static byte[] JsonViaHelper(string serializedJson) => PhysicalHelper(serializedJson);
            public static byte[] JsonViaWrapper(string serializedJson) => JsonViaHelper(serializedJson);
        }
        public static class SHA256Shadow { public static byte[] HashData(byte[] value) => value; }
        public static class Innocent
        {
            public static byte[] SameSpelling(byte[] value) => SHA256Shadow.HashData(value);
            public static byte[] ExactlyNamedShadow(byte[] value) => System.Security.Cryptography.HashAlgorithm.ComputeHash(value);
        }
        }
        namespace System.Security.Cryptography { public class HashAlgorithm { public static byte[] ComputeHash(byte[] value) => value; } }
        """;

    private static readonly Lazy<JsonHashReferenceGraph> Fixture = new(() =>
        JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("JsonHashReferenceFixture", Source, [typeof(JsonHashInput).Assembly]))]));

    [Theory]
    [InlineData("Alias")]
    [InlineData("StaticImport")]
    public void Primitive_aliases_and_static_imports_resolve_to_the_actual_digest_type(string method)
    {
        var graph = Fixture.Value;
        Assert.Empty(graph.Errors);
        var reference = Assert.Single(graph.References.Where(item => Owner(item, method) && item.Role == "digest-primitive"));
        Assert.Contains("System.Security.Cryptography.SHA256::HashData(System.Byte[])", reference.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_sites_remain_distinct_and_shadow_names_do_not_become_primitives()
    {
        var graph = Fixture.Value;
        var sites = graph.References.Where(item => Owner(item, "Twice") && item.Role == "digest-primitive").ToArray();
        Assert.Equal([1, 2], sites.Select(item => item.Occurrence));
        Assert.NotEqual(sites[0].Offset, sites[1].Offset);
        Assert.DoesNotContain(graph.References, item => Owner(item, "SameSpelling") && item.Role != "ordinary");
        Assert.DoesNotContain(graph.References, item => Owner(item, "ExactlyNamedShadow") && item.Role != "ordinary");
    }

    [Theory]
    [InlineData("Field")]
    [InlineData("Property")]
    [InlineData("Dispatch")]
    [InlineData("VirtualDispatch")]
    [InlineData("JsonViaWrapper")]
    public void Closure_reaches_real_hashes_through_fields_accessors_interfaces_virtual_calls_and_wrappers(string method)
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values.Where(item => item.Type == "HashFixture.Recipes" && item.Name == method));
        var closure = graph.ReachableDefinitions(entry.Identity);
        Assert.Contains(graph.References, item => closure.Contains(item.Owner) && item.Role == "digest-primitive");
    }

    [Fact]
    public void A_new_caller_of_an_existing_physical_helper_changes_the_observed_caller_set()
    {
        var original = Fixture.Value;
        var helper = Assert.Single(original.Definitions.Values.Where(item => item.Name == "PhysicalHelper"));
        var modified = Source.Replace("public static byte[] JsonViaHelper", "public static byte[] NewlyAdmitted(string json) => PhysicalHelper(json); public static byte[] JsonViaHelper", StringComparison.Ordinal);
        var after = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("JsonHashReferenceFixture", modified, [typeof(JsonHashInput).Assembly]))]);
        Assert.Empty(after.Errors);
        Assert.Single(original.ObservedCallers(helper.Identity));
        Assert.Equal(2, after.ObservedCallers(helper.Identity).Count);
    }

    [Fact]
    public void An_injected_delegate_retains_its_constructor_producers()
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values.Where(item => item.Type == "HashFixture.InjectedRecipe" && item.Name == "Run"));
        var closure = graph.ReachableDefinitions(entry.Identity);
        Assert.Contains(graph.References, item => closure.Contains(item.Owner) && item.Role == "digest-primitive" && item.Operation == "ldftn");
    }

    [Fact]
    public void An_unrelated_canonical_component_does_not_hide_a_raw_digest_reference()
    {
        var sites = Fixture.Value.References.Where(item => Owner(item, "Mixed")).ToArray();
        Assert.Contains(sites, item => item.Role == "json-hash-input");
        Assert.Contains(sites, item => item.Role == "digest-primitive");
    }

    [Fact]
    public void Gateway_overloads_and_generic_input_types_remain_explicit()
    {
        var direct = Assert.Single(Fixture.Value.References.Where(item => Owner(item, "Gateway") && item.Role == "json-hash-input"));
        var generic = Assert.Single(Fixture.Value.References.Where(item => Owner(item, "GenericGateway") && item.Role == "json-hash-input"));
        Assert.Contains("System.Text.Json.JsonElement", direct.Target, StringComparison.Ordinal);
        Assert.Equal(["System.String"], generic.GenericArguments);
        Assert.NotEqual(direct.Target, generic.Target);
    }

    [Fact]
    public void An_indirect_function_pointer_is_an_explicit_unresolved_finding()
    {
        const string source = """
            public static unsafe class PointerRecipe
            {
                public static byte[] Run(byte[] input)
                {
                    delegate*<byte[], byte[]> hash = &System.Security.Cryptography.SHA256.HashData;
                    return hash(input);
                }
            }
            """;
        var image = FixtureCompiler.Compile("JsonHashPointerFixture", source, allowUnsafe: true);
        var graph = JsonHashReferenceGraph.Scan([Assembly.Load(image)]);
        Assert.Contains(graph.Errors, error => error.Contains("Unresolved indirect function-pointer call", StringComparison.Ordinal));
        Assert.Contains(graph.References, reference => reference.Role == "digest-primitive");
    }

    private static bool Owner(JsonHashReferenceGraph.Reference reference, string method) => reference.Owner.Contains("::" + method + "(", StringComparison.Ordinal);
}
