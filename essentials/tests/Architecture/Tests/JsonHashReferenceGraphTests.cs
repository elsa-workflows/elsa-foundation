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
        public class RootSlot { public virtual byte[] Compute(byte[] value) => value; }
        public class MiddleSlot : RootSlot { public override byte[] Compute(byte[] value) => value; }
        public class BottomSlot : MiddleSlot { public override byte[] Compute(byte[] value) => SHA256.HashData(value); }
        public class SeparateSlot : MiddleSlot { public new virtual byte[] Compute(byte[] value) => SHA256.HashData(value); }
        public class InjectedRecipe
        {
            private readonly Func<byte[], byte[]> algorithm;
            public InjectedRecipe(Func<byte[], byte[]> algorithm) { this.algorithm = algorithm; }
            public byte[] Run(byte[] value) => algorithm(value);
        }
        public interface IConfiguredRecipe { void Set(Func<byte[], byte[]> algorithm); }
        public class ConfiguredRecipe : IConfiguredRecipe
        {
            private Func<byte[], byte[]> algorithm = null!;
            public void Set(Func<byte[], byte[]> value) { algorithm = value; }
            public byte[] Run(byte[] value) => algorithm(value);
        }
        public class ExternalDigest : ICryptoTransform
        {
            public int InputBlockSize => 1; public int OutputBlockSize => 1;
            public bool CanTransformMultipleBlocks => true; public bool CanReuseTransform => true;
            public int TransformBlock(byte[] input, int offset, int count, byte[] output, int outputOffset) => 0;
            public byte[] TransformFinalBlock(byte[] input, int offset, int count) => SHA256.HashData(input);
            public void Dispose() { }
        }
        public class ExternalStream : System.IO.MemoryStream { public override void Write(byte[] value, int offset, int count) { _ = SHA256.HashData(value); } }
        public class Number
        {
            public static implicit operator int(Number value) => 1;
            public static implicit operator long(Number value) => 2;
        }
        public class GenericRecipe<T> { public static string Run(T value) => JsonHashInput.Hash(value); }
        public class ExternalGenericDigest<T> : System.Collections.Generic.IEqualityComparer<T>
        {
            public bool Equals(T left, T right) => true;
            public int GetHashCode(T value) => SHA256.HashData(new byte[] { 1 })[0];
        }
        public static class Recipes
        {
            private static readonly Func<byte[], byte[]> Algorithm = SHA256.HashData;
            private static Func<byte[], byte[]> AlgorithmProperty => Algorithm;
            public static byte[] Alias(byte[] value) => Digest.HashData(value);
            public static byte[] StaticImport(byte[] value) => HashData(value);
            public static byte[] Incremental(byte[] value)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(value);
                return hash.GetHashAndReset();
            }
            public static byte[] NonCryptographic(byte[] value) => System.IO.Hashing.Crc32.Hash(value);
            public static byte[] Twice(byte[] value) { _ = SHA256.HashData(value); return SHA256.HashData(value); }
            public static byte[] Field(byte[] value) => Algorithm(value);
            public static byte[] Property(byte[] value) => AlgorithmProperty(value);
            public static byte[] Dispatch(IDigest algorithm, byte[] value) => algorithm.Compute(value);
            public static byte[] VirtualDispatch(DigestBase algorithm, byte[] value) => algorithm.Compute(value);
            public static byte[] ThreeLevelDispatch(MiddleSlot algorithm, byte[] value) => algorithm.Compute(value);
            public static InjectedRecipe Factory() => new(SHA256.HashData);
            public static InjectedRecipe TwoStageFactory() => Build(SHA256.HashData);
            private static InjectedRecipe Build(Func<byte[], byte[]> algorithm) => new(algorithm);
            public static InjectedRecipe UnrelatedCallerOfFactory() => TwoStageFactory();
            public static void ConfigureViaInterface(IConfiguredRecipe recipe) => recipe.Set(SHA256.HashData);
            public static byte[] ExternalInterface(ICryptoTransform algorithm, byte[] value) => algorithm.TransformFinalBlock(value, 0, value.Length);
            public static void ExternalVirtual(System.IO.Stream algorithm, byte[] value) => algorithm.Write(value, 0, value.Length);
            public static int ExternalGeneric(System.Collections.Generic.IEqualityComparer<string> algorithm) => algorithm.GetHashCode("value");
            public static string ClosedDeclaringTypes(System.Text.Json.JsonElement json, string text) => GenericRecipe<System.Text.Json.JsonElement>.Run(json) + GenericRecipe<string>.Run(text);
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
        JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("JsonHashReferenceFixture", Source, [typeof(JsonHashInput).Assembly, typeof(System.IO.Hashing.Crc32).Assembly]))]));

    [Theory]
    [InlineData("Alias")]
    [InlineData("StaticImport")]
    public void Primitive_aliases_and_static_imports_resolve_to_the_actual_digest_type(string method)
    {
        var graph = Fixture.Value;
        Assert.Empty(graph.Errors);
        var reference = Assert.Single(graph.References, item => Owner(item, method) && item.Role == "digest-primitive");
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
    [InlineData("ExternalInterface")]
    [InlineData("ExternalVirtual")]
    [InlineData("ExternalGeneric")]
    [InlineData("JsonViaWrapper")]
    public void Closure_reaches_real_hashes_through_fields_accessors_interfaces_virtual_calls_and_wrappers(string method)
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.Recipes" && item.Name == method);
        var closure = graph.ReachableDefinitions(entry.Identity);
        Assert.Contains(graph.References, item => closure.Contains(item.Owner) && item.Role == "digest-primitive");
    }

    [Fact]
    public void A_new_caller_of_an_existing_physical_helper_changes_the_observed_caller_set()
    {
        var original = Fixture.Value;
        var helper = Assert.Single(original.Definitions.Values, item => item.Name == "PhysicalHelper");
        var modified = Source.Replace("public static byte[] JsonViaHelper", "public static byte[] NewlyAdmitted(string json) => PhysicalHelper(json); public static byte[] JsonViaHelper", StringComparison.Ordinal);
        var after = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("JsonHashReferenceFixture", modified, [typeof(JsonHashInput).Assembly, typeof(System.IO.Hashing.Crc32).Assembly]))]);
        Assert.Empty(after.Errors);
        Assert.Single(original.ObservedCallers(helper.Identity));
        Assert.Equal(2, after.ObservedCallers(helper.Identity).Count);
    }

    [Fact]
    public void An_injected_delegate_retains_its_constructor_producers()
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.InjectedRecipe" && item.Name == "Run");
        var closure = graph.ReachableDefinitions(entry.Identity);
        Assert.Contains(graph.References, item => closure.Contains(item.Owner) && item.Role == "digest-primitive" && item.Operation == "ldftn");
        Assert.Contains(closure, item => item.Contains("::TwoStageFactory()", StringComparison.Ordinal));
        Assert.DoesNotContain(closure, item => item.Contains("::UnrelatedCallerOfFactory()", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unrelated_canonical_component_does_not_hide_a_raw_digest_reference()
    {
        var sites = Fixture.Value.References.Where(item => Owner(item, "Mixed")).ToArray();
        Assert.Contains(sites, item => item.Role == "json-hash-input");
        Assert.Contains(sites, item => item.Role == "digest-primitive");
    }

    [Fact]
    public void Actual_incremental_hash_operations_are_not_mistaken_for_ordinary_calls()
    {
        var references = Fixture.Value.References.Where(item => Owner(item, "Incremental") && item.Role == "digest-primitive").ToArray();
        foreach (var name in new[] { "CreateHash", "AppendData", "GetHashAndReset" })
            Assert.Contains(references, reference => reference.Target.Contains("System.Security.Cryptography.IncrementalHash::" + name, StringComparison.Ordinal));
    }

    [Fact]
    public void A_real_noncryptographic_algorithm_keeps_its_actual_primitive_identity()
    {
        var reference = Assert.Single(Fixture.Value.References, item => Owner(item, "NonCryptographic") && item.Role == "digest-primitive");
        Assert.Contains("System.IO.Hashing|System.IO.Hashing.Crc32::Hash", reference.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway_overloads_and_generic_input_types_remain_explicit()
    {
        var direct = Assert.Single(Fixture.Value.References, item => Owner(item, "Gateway") && item.Role == "json-hash-input");
        var generic = Assert.Single(Fixture.Value.References, item => Owner(item, "GenericGateway") && item.Role == "json-hash-input");
        Assert.Contains("System.Text.Json.JsonElement", direct.Target, StringComparison.Ordinal);
        Assert.Equal(["System.String"], generic.GenericArguments);
        Assert.NotEqual(direct.Target, generic.Target);
    }

    [Fact]
    public void Constructed_declaring_types_are_retained_separately_from_shared_method_definitions()
    {
        var calls = Fixture.Value.References.Where(reference => Owner(reference, "ClosedDeclaringTypes") && reference.Target.Contains("GenericRecipe", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].Target, calls[1].Target);
        Assert.NotEqual(calls[0].DeclaringTypeContext, calls[1].DeclaringTypeContext);
        Assert.Contains(calls, call => call.DeclaringTypeContext.Contains("System.Text.Json.JsonElement", StringComparison.Ordinal));
        Assert.Contains(calls, call => call.DeclaringTypeContext.Contains("System.String", StringComparison.Ordinal));
    }

    [Fact]
    public void Valid_conversion_operators_with_the_same_parameters_keep_distinct_return_contracts()
    {
        var graph = Fixture.Value;
        Assert.Empty(graph.Errors);
        var operators = graph.Definitions.Values.Where(item => item.Type == "HashFixture.Number" && item.Name == "op_Implicit").ToArray();
        Assert.Equal(2, operators.Length);
        Assert.NotEqual(operators[0].Identity, operators[1].Identity);
    }

    [Fact]
    public void An_exact_name_impostor_does_not_acquire_gateway_authority()
    {
        const string source = """
            namespace Elsa.Serialization.Core { public static class JsonHashInput { public static string Hash(object value) => "impostor"; } }
            public static class Caller { public static string Run(object value) => Elsa.Serialization.Core.JsonHashInput.Hash(value); }
            """;
        var graph = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("Elsa.Serialization.Core", source))]);
        Assert.Empty(graph.Errors);
        Assert.Contains(graph.References, reference => reference.Target.Contains("JsonHashInput::Hash", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.References, reference => reference.Role == "json-hash-input");
    }

    [Fact]
    public void An_exact_name_impostor_does_not_become_the_noncryptographic_primitive()
    {
        const string source = """
            namespace System.IO.Hashing { public class NonCryptographicHashAlgorithm { public static byte[] Hash(byte[] value) => value; } }
            public static class Caller { public static byte[] Run(byte[] value) => System.IO.Hashing.NonCryptographicHashAlgorithm.Hash(value); }
            """;
        var graph = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("System.IO.Hashing", source))]);
        Assert.Empty(graph.Errors);
        Assert.Contains(graph.References, reference => reference.Target.Contains("NonCryptographicHashAlgorithm::Hash", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.References, reference => reference.Role == "digest-primitive");
    }

    [Fact]
    public void A_signed_digest_in_a_private_host_context_remains_a_primitive()
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("S9 private digest " + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var privatePackage = context.LoadFromAssemblyPath(typeof(System.IO.Hashing.Crc32).Assembly.Location);
            const string source = "public static class PrivateRecipe { public static byte[] Run(byte[] value) => System.IO.Hashing.Crc32.Hash(value); }";
            var image = FixtureCompiler.Compile("S9.PrivateDigest." + Guid.NewGuid().ToString("N"), source, [typeof(System.IO.Hashing.Crc32).Assembly]);
            using var stream = new MemoryStream(image);
            var assembly = context.LoadFromStream(stream);
            var privateDigest = privatePackage.GetType("System.IO.Hashing.Crc32")!;
            Assert.False(typeof(System.IO.Hashing.NonCryptographicHashAlgorithm).IsAssignableFrom(privateDigest));
            var graph = JsonHashReferenceGraph.Scan([assembly]);
            Assert.Empty(graph.Errors);
            Assert.Contains(graph.References, reference => Owner(reference, "Run") && reference.Role == "digest-primitive" && reference.Target.Contains("System.IO.Hashing.Crc32::Hash", StringComparison.Ordinal));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void An_unrelated_anonymous_type_does_not_renumber_a_hash_site_contract()
    {
        const string consumer = "public static class Consumer { public static string Run() => Elsa.Serialization.Core.JsonHashInput.Hash(new { Name = \"René\", Count = 1 }); }";
        const string unrelated = "public static class Unrelated { public static object Other() => new { Different = true }; }";
        var before = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("S9.AnonymousReceipts", consumer, [typeof(JsonHashInput).Assembly]))]);
        var after = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("S9.AnonymousReceipts", unrelated + consumer, [typeof(JsonHashInput).Assembly]))]);
        Assert.Empty(before.Errors); Assert.Empty(after.Errors);
        var original = Assert.Single(before.References, reference => Owner(reference, "Run") && reference.Role == "json-hash-input");
        var changed = Assert.Single(after.References, reference => Owner(reference, "Run") && reference.Role == "json-hash-input");
        Assert.Equal(original.GenericArguments, changed.GenericArguments);
        Assert.DoesNotContain(original.GenericArguments, argument => argument.Contains("<>f__AnonymousType", StringComparison.Ordinal));
        var altered = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("S9.AnonymousReceipts", consumer.Replace("Count = 1", "Count = 1L", StringComparison.Ordinal), [typeof(JsonHashInput).Assembly]))]);
        var alteredSite = Assert.Single(altered.References, reference => Owner(reference, "Run") && reference.Role == "json-hash-input");
        Assert.NotEqual(original.GenericArguments, alteredSite.GenericArguments);
    }

    [Fact]
    public void Incoming_receipts_retain_interface_call_sites_and_context()
    {
        var graph = Fixture.Value;
        var implementation = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.ActualDigest" && item.Name == "Compute");
        var incoming = graph.IncomingReferences(implementation.Identity);
        Assert.Contains(incoming.References, reference => Owner(reference, "Dispatch") && reference.Operation == "callvirt" && reference.Occurrence == 1);
        Assert.Contains(incoming.Bindings, binding => binding.Kind == "dispatch" && binding.Target == implementation.Identity && binding.Source.Contains("HashFixture.IDigest::Compute", StringComparison.Ordinal));
        Assert.NotEqual(Guid.Empty, implementation.ModuleMvid);
        Assert.Equal(0x06000000, implementation.MetadataToken & unchecked((int)0xff000000));
    }

    [Fact]
    public void Structured_closures_distinguish_internal_sources_external_leaves_and_authority_boundaries()
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.Recipes" && item.Name == "Gateway");
        var gateways = graph.References.Where(reference => reference.Role == "json-hash-input").Select(reference => reference.Target).ToHashSet(StringComparer.Ordinal);
        var closure = graph.InspectClosure(entry.Identity, gateways);
        Assert.Contains(entry.Identity, closure.InternalDefinitions);
        Assert.NotEmpty(closure.Boundaries);
        Assert.DoesNotContain(closure.References, reference => closure.Boundaries.Contains(reference.Owner));
        Assert.All(closure.InternalDefinitions, identity => Assert.Contains(identity, graph.Definitions.Keys));
        Assert.All(closure.ExternalLeaves, identity => Assert.DoesNotContain(identity, graph.Definitions.Keys));
        var dispatched = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.Recipes" && item.Name == "Dispatch");
        Assert.Contains(graph.InspectClosure(dispatched.Identity).Bindings, binding => binding.Kind == "dispatch");
    }

    [Fact]
    public void Delegate_setter_producers_retain_interface_dispatch_callers()
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.ConfiguredRecipe" && item.Name == "Run");
        var closure = graph.InspectClosure(entry.Identity);
        Assert.Contains(closure.References, reference => Owner(reference, "ConfigureViaInterface") && reference.Role == "digest-primitive");
    }

    [Fact]
    public void Intermediate_virtual_slots_reach_deeper_overrides_without_crossing_newslot()
    {
        var graph = Fixture.Value;
        var entry = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.Recipes" && item.Name == "ThreeLevelDispatch");
        var bottom = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.BottomSlot" && item.Name == "Compute");
        var middle = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.MiddleSlot" && item.Name == "Compute");
        var separate = Assert.Single(graph.Definitions.Values, item => item.Type == "HashFixture.SeparateSlot" && item.Name == "Compute");
        var closure = graph.InspectClosure(entry.Identity);
        Assert.Contains(bottom.Identity, closure.InternalDefinitions);
        Assert.Contains(bottom.Identity, graph.ReachableDefinitions(middle.Identity));
        Assert.DoesNotContain(separate.Identity, closure.InternalDefinitions);
        Assert.Contains(graph.IncomingReferences(bottom.Identity).References, reference => Owner(reference, "ThreeLevelDispatch"));
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
