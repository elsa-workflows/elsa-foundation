using System.Reflection;
using System.Runtime.Loader;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashRecipeReceiptTests
{
    [Fact]
    public void Streaming_receipt_digest_matches_the_buffered_JSON_bytes_across_many_buffer_flushes()
    {
        var value = Enumerable.Range(0, 20_000).Select(index => new { Index = index, Text = "René <>& \\ " + index }).ToArray();
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value)));
        Assert.Equal(expected, JsonHashRecipeReceipt.Digest(value));
    }

    private const string Owner = """
        public static class Recipe {
            public static string Hash() => System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Payload())));
        }
        """;

    [Fact]
    public void A_computed_DTO_getter_in_another_source_file_is_part_of_the_recipe()
    {
        using var a = new Fixture(Owner, "public struct Payload { public string Value => \"before\"; }");
        using var b = new Fixture(Owner, "public struct Payload { public string Value => \"after\"; }");
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        Assert.Contains(a.Receipt().SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_runtime_type_token_keeps_its_closed_generic_payload_contract(bool converter)
    {
        const string owner = """
            public static class Recipe {
                public static string Hash(object value) => Elsa.Serialization.Core.JsonHashInput.Hash(
                    Elsa.Serialization.Core.JsonHashInput.SerializeToElement(value, typeof(System.Collections.Generic.Dictionary<string, Payload>)));
            }
            """;
        var payload = converter ? """
            [System.Text.Json.Serialization.JsonConverter(typeof(PayloadConverter))]
            public struct Payload { }
            public sealed class PayloadConverter : System.Text.Json.Serialization.JsonConverter<Payload> {
                public override Payload Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => default;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Payload value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(41);
            }
            """ : "public struct Payload { public int Value => 41; }";
        using var a = new Fixture(owner, payload, includeAuthority: true);
        using var b = new Fixture(owner, payload.Replace("41", "42", StringComparison.Ordinal), includeAuthority: true);
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        Assert.Contains(a.Receipt().ContractIdentities, name => name.Contains("Payload", StringComparison.Ordinal));
        Assert.Contains(a.Receipt().SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Union_receipts_ignore_root_order_but_bind_every_root_and_its_shared_DTO_sources()
    {
        const string helper = "public static class Extra { public static string Run() => Recipe.Hash(); }";
        using var a = new Fixture(Owner, "public struct Payload { public int Value => 42; }", helper);
        using var b = new Fixture(Owner, "public struct Payload { public int Value => 43; }", helper);
        var extra = a.Graph.Definitions.Values.Single(definition => definition.Type == "Extra" && definition.Name == "Run").Identity;
        JsonHashRecipeReceipt.Receipt Union(Fixture fixture, params string[] entries) =>
            JsonHashRecipeReceipt.CreateMany(entries, fixture.Graph, fixture.Assemblies, fixture.Maps, fixture.ExternalSources);
        Assert.Equal(Union(a, a.Entry, extra).Signature, Union(a, extra, a.Entry).Signature);
        Assert.NotEqual(Union(a, a.Entry).Signature, Union(a, a.Entry, extra).Signature);
        Assert.NotEqual(Union(a, a.Entry, extra).Signature, Union(b, b.Entry, extra).Signature);
    }

    [Fact]
    public void Closed_generic_converter_helpers_in_another_file_cannot_borrow_a_receipt()
    {
        const string payload = """
            [System.Text.Json.Serialization.JsonConverter(typeof(Converter<Payload>))]
            public struct Payload { public int Value => 1; }
            public sealed class Converter<T> : System.Text.Json.Serialization.JsonConverter<T> {
                public Converter() { }
                public override T Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => default!;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue(Helper.Text());
            }
            """;
        using var a = new Fixture(Owner, payload, "public static class Helper { public static string Text() => \"before\"; }");
        using var b = new Fixture(Owner, payload, "public static class Helper { public static string Text() => \"after\"; }");
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        Assert.Contains(a.Receipt().SourcePaths, path => path.EndsWith("Helper.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Recursive_generic_schemas_terminate_with_their_argument_binding_retained()
    {
        using var fixture = new Fixture(Owner.Replace("new Payload()", "new Payload<int>()", StringComparison.Ordinal), """
            public class Payload<T> {
                public Payload() { }
                public Payload<System.Collections.Generic.List<T>>? Next => null;
            }
            """);
        var receipt = fixture.Receipt();
        Assert.InRange(receipt.Contracts, 1, 60);
        Assert.Contains(receipt.ContractIdentities, name => name.Contains("Payload`1[System.Int32]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_converter_attribute_factory_in_another_file_is_part_of_the_recipe()
    {
        const string payload = """
            [Selector] public struct Payload { public int Value => 1; }
            public sealed partial class SelectorAttribute : System.Text.Json.Serialization.JsonConverterAttribute {
                public SelectorAttribute() { }
            }
            public sealed class ConverterA : System.Text.Json.Serialization.JsonConverter<Payload> {
                public ConverterA() { }
                public override Payload Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => default;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Payload value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(1);
            }
            public sealed class ConverterB : System.Text.Json.Serialization.JsonConverter<Payload> {
                public ConverterB() { }
                public override Payload Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => default;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, Payload value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(2);
            }
            """;
        const string factory = """
            public sealed partial class SelectorAttribute {
                public override System.Text.Json.Serialization.JsonConverter CreateConverter(System.Type type) => new ConverterA();
            }
            """;
        using var a = new Fixture(Owner, payload, factory);
        using var b = new Fixture(Owner, payload, factory.Replace("ConverterA", "ConverterB", StringComparison.Ordinal));
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        Assert.Contains(a.Receipt().SourcePaths, path => path.EndsWith("Helper.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_type_named_like_the_authority_in_another_module_is_a_regular_dependency()
    {
        const string owner = "public static class Recipe { public static string Hash() => Elsa.Serialization.Core.JsonHashInputAdapter.Hash(); }";
        const string payload = """
            namespace Elsa.Serialization.Core {
                public static class JsonHashInputAdapter { public static string Hash() => "before"; }
            }
            """;
        using var a = new Fixture(owner, payload);
        using var b = new Fixture(owner, payload.Replace("before", "after", StringComparison.Ordinal));
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        Assert.Contains(a.Receipt().DefinitionIdentities, name => name.Contains("JsonHashInputAdapter::Hash", StringComparison.Ordinal));
    }

    [Fact]
    public void Compiler_lowered_collection_enumerators_keep_semantic_body_witnesses_without_claiming_authored_documents()
    {
        using var fixture = new Fixture(Owner, """
            public struct Payload {
                public System.Collections.Generic.IReadOnlyList<int> Values => [1];
                public int First { get { using var enumerator = Values.GetEnumerator(); return enumerator.MoveNext() ? enumerator.Current : 0; } }
            }
            """);
        var receipt = fixture.Receipt();
        Assert.Contains(receipt.DefinitionIdentities, name => name.Contains("<>z__ReadOnlySingleElementList`1+Enumerator", StringComparison.Ordinal));
        Assert.Contains(receipt.SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Implicit_empty_converter_constructors_keep_exact_body_and_base_contract_witnesses()
    {
        using var a = new Fixture(Owner, "public sealed class Payload { public int Value => 1; }");
        var constructor = a.Graph.Definitions.Values.Single(d => d.Type == "Payload" && d.Name == ".ctor");
        Assert.Empty(a.Maps.Values.Single().Methods[constructor.MetadataToken].Documents);
        Assert.Contains(constructor.Identity, a.Receipt().DefinitionIdentities);
        using var b = new Fixture(Owner, "public sealed class Payload { public Payload() { System.GC.KeepAlive(42); } public int Value => 1; }");
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
    }

    [Fact]
    public void A_closed_generic_converter_base_is_resolved_exactly_for_its_implicit_constructor()
    {
        using var fixture = new Fixture(Owner, """
            public sealed class Payload : System.Text.Json.Serialization.JsonConverter<int> {
                public override int Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => 0;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options) => writer.WriteNumberValue(value);
            }
            """);
        var constructor = fixture.Graph.Definitions.Values.Single(d => d.Type == "Payload" && d.Name == ".ctor");
        Assert.Empty(fixture.Maps.Values.Single().Methods[constructor.MetadataToken].Documents);
        Assert.Contains(constructor.Identity, fixture.Receipt().DefinitionIdentities);
    }

    [Fact]
    public void External_base_callbacks_keep_first_party_overrides_through_the_full_base_chain()
    {
        const string source = """
            public class Intermediate : System.IO.MemoryStream { }
            public sealed class Payload : Intermediate {
                public int Value => 1;
                public override void WriteByte(byte value) { System.GC.KeepAlive(42); }
            }
            """;
        using var a = new Fixture(Owner, source);
        using var b = new Fixture(Owner, source.Replace("42", "43", StringComparison.Ordinal));
        Assert.Contains(a.Receipt().DefinitionIdentities, name => name.Contains("Payload::WriteByte", StringComparison.Ordinal));
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
    }

    [Fact]
    public void Hidden_authored_bodies_bind_the_compiler_authenticated_declaring_source()
    {
        const string source = """
            public sealed class Payload {
            #line hidden
                public int Value => 42;
            #line default
            }
            """;
        using var a = new Fixture(Owner, source);
        using var b = new Fixture(Owner, source.Replace("42", "43", StringComparison.Ordinal));
        var getter = a.Graph.Definitions.Values.Single(definition => definition.Type == "Payload" && definition.Name == "get_Value");
        var map = a.Maps.Values.Single();
        Assert.Empty(map.Methods[getter.MetadataToken].Documents);
        Assert.Contains("Payload.cs", map.TypeDocuments[a.Assembly.GetType("Payload")!.MetadataToken]);
        Assert.Contains(a.Receipt().SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
        File.Delete(Path.Join(a.Root, "Payload.cs"));
        var failed = JsonHashSourceDocuments.Read(a.ImagePath, a.Root);
        Assert.NotEmpty(failed.Errors);
        Assert.Contains(failed.Documents.Keys, path => path == "Owner.cs");
        Assert.Throws<InvalidOperationException>(() => JsonHashRecipeReceipt.Create(a.Entry, a.Graph, a.Assemblies,
            new Dictionary<Guid, JsonHashSourceDocuments> { [a.Assembly.ManifestModule.ModuleVersionId] = failed }));
    }

    [Fact]
    public void A_documented_member_also_binds_its_other_partial_type_declarations()
    {
        using var fixture = new Fixture(Owner, "public partial struct Payload { public int Value => 1; }",
            "public partial struct Payload { }");
        var receipt = fixture.Receipt();
        Assert.Contains(receipt.SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
        Assert.Contains(receipt.SourcePaths, path => path.EndsWith("Helper.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_external_base_static_interface_callbacks_keep_their_shipping_helpers()
    {
        var externalImage = FixtureCompiler.Compile("ExternalCallbacks" + Guid.NewGuid().ToString("N"), """
            public interface ISeed<T> where T : ISeed<T> { static abstract int Seed(); }
            public class ExternalBase<T> where T : ISeed<T> {
                protected ExternalBase() { Stored = T.Seed(); }
                public int Stored { get; }
            }
            """);
        const string payload = """
            public sealed class Payload<U> : ExternalBase<Payload<U>>, ISeed<Payload<U>> {
                public static int Seed() => SeparateHelper.Value();
                public int Value => Stored;
            }
            """;
        using var a = new Fixture(Owner.Replace("new Payload()", "new Payload<int>()", StringComparison.Ordinal), payload,
            "public static class SeparateHelper { public static int Value() => 42; }", referenceImages: [externalImage]);
        using var b = new Fixture(Owner.Replace("new Payload()", "new Payload<int>()", StringComparison.Ordinal), payload,
            "public static class SeparateHelper { public static int Value() => 43; }", referenceImages: [externalImage]);
        Assert.Contains(a.Receipt().DefinitionIdentities, name => name.Contains("SeparateHelper::Value", StringComparison.Ordinal));
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
    }

    [Fact]
    public void A_record_struct_primary_constructor_is_proved_as_exact_parameter_to_backing_field_assignments()
    {
        using var fixture = new Fixture(Owner, "public readonly record struct Payload(int Kind, string Text);");
        var constructor = fixture.Graph.Definitions.Values.Single(d => d.Type == "Payload" && d.Name == ".ctor");
        Assert.Empty(fixture.Maps.Values.Single().Methods[constructor.MetadataToken].Documents);
        Assert.Contains(constructor.Identity, fixture.Receipt().DefinitionIdentities);
        using var changed = new Fixture(Owner, """
            public readonly record struct Payload {
                public int Kind { get; init; } public string Text { get; init; }
                public Payload(int Kind, string Text) { this.Kind = Kind + 1; this.Text = Text; }
            }
            """);
        var changedConstructor = changed.Assembly.GetType("Payload")!.GetConstructors().Single();
        Assert.False(JsonHashRecipeReceipt.CanWitnessWithoutDocument(changedConstructor));
        Assert.NotEqual(fixture.Receipt().Signature, changed.Receipt().Signature);
    }

    [Fact]
    public void Iterator_kickoff_binds_its_source_through_the_authenticated_PDB_state_machine_relation()
    {
        using var fixture = new Fixture(Owner, """
            public struct Payload {
                private static System.Collections.Generic.IEnumerable<int> Values() { yield return 1; }
                public int First => System.Linq.Enumerable.First(Values());
            }
            """);
        var iterator = fixture.Graph.Definitions.Values.Single(d => d.Type == "Payload" && d.Name == "Values");
        var map = fixture.Maps[fixture.Assembly.ManifestModule.ModuleVersionId];
        Assert.Empty(map.Methods[iterator.MetadataToken].Documents);
        Assert.Contains(map.Methods.Values, method => method.KickoffToken == iterator.MetadataToken && method.Documents.Count != 0);
        var receipt = fixture.Receipt();
        Assert.Contains(iterator.Identity, receipt.DefinitionIdentities);
        Assert.Contains(receipt.SourcePaths, path => path.EndsWith("Payload.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_actual_hash_authority_is_a_consumer_boundary_but_is_included_when_reviewed_as_its_own_root(bool privateAuthority)
    {
        const string owner = "public static class Recipe { public static string Hash() => Elsa.Serialization.Core.JsonHashInput.Hash(new Payload()); }";
        using var fixture = new Fixture(owner, "public struct Payload { public int Value => 1; }", includeAuthority: true, privateAuthority: privateAuthority);
        Assert.Contains(fixture.Graph.References, reference => reference.Owner == fixture.Entry && reference.Role == "json-hash-input");
        if (privateAuthority) Assert.DoesNotContain(typeof(JsonHashInput).Assembly, fixture.Assemblies);
        var consumer = fixture.Receipt();
        var authority = typeof(JsonHashInput);
        var definitions = fixture.Graph.Definitions.Values.Where(d => d.ModuleMvid == authority.Module.ModuleVersionId &&
            (d.RuntimeType == authority.FullName || d.RuntimeType?.StartsWith(authority.FullName + "+", StringComparison.Ordinal) == true)).ToArray();
        Assert.NotEmpty(definitions);
        Assert.DoesNotContain(consumer.DefinitionIdentities, name => definitions.Any(d => d.Identity == name));
        Assert.DoesNotContain(consumer.SourcePaths, path => path.EndsWith("/JsonHashInput.cs", StringComparison.Ordinal));
        Assert.Contains(consumer.ContractIdentities, name => name.Contains("Elsa.Serialization.Core.JsonHashInput", StringComparison.Ordinal));
        var entry = definitions.Single(d => d.Name == "Hash" && d.Identity.Contains("(System.Text.Json.JsonElement)", StringComparison.Ordinal)).Identity;
        Assert.Throws<InvalidOperationException>(() => JsonHashRecipeReceipt.CreateMany([fixture.Entry, entry], fixture.Graph, fixture.Assemblies, fixture.Maps, fixture.ExternalSources));
        var reviewed = JsonHashRecipeReceipt.Create(entry, fixture.Graph, fixture.Assemblies, fixture.Maps, fixture.ExternalSources);
        Assert.Contains(entry, reviewed.DefinitionIdentities);
        Assert.Contains(reviewed.SourcePaths, path => path.EndsWith("/JsonHashInput.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_and_failed_source_maps_and_unknown_owners_are_refused()
    {
        using var fixture = new Fixture(Owner, "public struct Payload { public int Value => 1; }");
        Assert.Throws<InvalidOperationException>(() => JsonHashRecipeReceipt.Create(fixture.Entry, fixture.Graph, [fixture.Assembly], new Dictionary<Guid, JsonHashSourceDocuments>()));
        Assert.Throws<InvalidOperationException>(() => JsonHashRecipeReceipt.Create("missing", fixture.Graph, [fixture.Assembly], fixture.Maps));
        File.AppendAllText(Path.Join(fixture.Root, "Payload.cs"), "// changed after compilation");
        var failed = JsonHashSourceDocuments.Read(fixture.ImagePath, fixture.Root);
        Assert.NotEmpty(failed.Errors);
        Assert.Throws<InvalidOperationException>(() => JsonHashRecipeReceipt.Create(fixture.Entry, fixture.Graph, [fixture.Assembly],
            new Dictionary<Guid, JsonHashSourceDocuments> { [fixture.Assembly.ManifestModule.ModuleVersionId] = failed }));
    }

    [Fact]
    public void Formatting_only_source_line_endings_share_a_receipt_after_separate_exact_authentication()
    {
        const string payload = "public struct Payload {\n public int Value => 1;\n}\n";
        using var lf = new Fixture(Owner.Replace("\r\n", "\n", StringComparison.Ordinal), payload);
        using var crlf = new Fixture(Owner.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal),
            payload.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.NotEqual(lf.Maps.Values.Single().Documents["Payload.cs"].Sha256, crlf.Maps.Values.Single().Documents["Payload.cs"].Sha256);
        Assert.Equal(lf.Receipt().Signature, crlf.Receipt().Signature);
    }

    [Fact]
    public void Literal_line_endings_remain_exact_even_when_document_formatting_is_portable()
    {
        const string payload = "public struct Payload { public string Value => \"\\r\\n\"; }";
        using var crlf = new Fixture(Owner, payload);
        using var lf = new Fixture(Owner, payload.Replace("\\r\\n", "\\n", StringComparison.Ordinal));
        Assert.NotEqual(crlf.Receipt().Signature, lf.Receipt().Signature);
        const string raw = "public struct Payload {\n public string Value => \"\"\"\nfirst\nsecond\n\"\"\";\n}\n";
        using var rawLf = new Fixture(Owner, raw);
        using var rawCrlf = new Fixture(Owner, raw.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Equal(rawLf.Maps.Values.Single().Documents["Payload.cs"].PortableSha256, rawCrlf.Maps.Values.Single().Documents["Payload.cs"].PortableSha256);
        Assert.NotEqual(rawLf.Receipt().Signature, rawCrlf.Receipt().Signature);
    }

    [Theory]
    [InlineData("constant")]
    [InlineData("attribute")]
    [InlineData("default")]
    public void Identical_authenticated_source_cannot_hide_changed_compiled_metadata(string kind)
    {
        const string choice = "\n#if ALTERNATE\n\"\\n\"\n#else\n\"\\r\\n\"\n#endif\n";
        var payload = kind switch
        {
            "constant" => "public struct Payload { public const string Marker = " + choice + "; public int Value => 1; }",
            "attribute" => "[System.Obsolete(" + choice + ")] public struct Payload { public int Value => 1; }",
            _ => "public struct Payload { public Payload(string value = " + choice + ") { } public int Value => 1; }"
        };
        using var a = new Fixture(Owner, payload);
        using var b = new Fixture(Owner, payload, symbols: ["ALTERNATE"]);
        Assert.Equal(a.Maps.Values.Single().Documents["Payload.cs"].Sha256, b.Maps.Values.Single().Documents["Payload.cs"].Sha256);
        Assert.NotEqual(a.Receipt().Signature, b.Receipt().Signature);
    }

    [Fact]
    public void Semantic_body_witness_binds_literal_contents_without_binding_metadata_token_numbers()
    {
        static string Witness(string source)
        {
            var assembly = Assembly.Load(FixtureCompiler.Compile("HashBodyFixture", source));
            return JsonHashRecipeReceipt.SemanticBody(assembly.GetType("Body")!.GetMethod("Value")!);
        }
        const string original = "public static class Body { public static string Value() => \"before\"; }";
        Assert.NotEqual(Witness(original), Witness(original.Replace("before", "after", StringComparison.Ordinal)));
        Assert.Equal(Witness(original), Witness("public static class Earlier { public static string Value() => \"unrelated\"; } " + original));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("elsa-s9-recipe-").FullName;
        internal string ImagePath { get; }
        internal Assembly Assembly { get; }
        internal IReadOnlyList<Assembly> Assemblies { get; }
        internal JsonHashReferenceGraph Graph { get; }
        internal string Entry { get; }
        internal Dictionary<Guid, JsonHashSourceDocuments> Maps { get; }
        internal IReadOnlyDictionary<string, string> ExternalSources { get; } = new Dictionary<string, string>();
        private readonly ResolveEventHandler? externalResolver;
        private readonly AssemblyLoadContext? privateContext;
        internal Fixture(string owner, string payload, string? helper = null, bool includeAuthority = false, IReadOnlyList<string>? symbols = null,
            IReadOnlyList<byte[]>? referenceImages = null, bool privateAuthority = false)
        {
            var paths = new List<string>();
            foreach (var (name, source) in new[] { ("Owner.cs", owner), ("Payload.cs", payload), ("Helper.cs", helper) })
            {
                if (source is null) continue;
                var path = Path.Join(Root, name); File.WriteAllText(path, source); paths.Add(path);
            }
            var image = referenceImages is not null ? FixtureCompiler.CompilePortableWithReferenceImages("HashReceiptFixture", referenceImages, paths.ToArray())
                : FixtureCompiler.CompilePortableConfigured("HashReceiptFixture", includeAuthority ? [typeof(JsonHashInput).Assembly] : [], symbols ?? [], paths.ToArray());
            ImagePath = Path.Join(Root, "HashReceiptFixture.dll");
            File.WriteAllBytes(ImagePath, image.Image); File.WriteAllBytes(Path.ChangeExtension(ImagePath, ".pdb"), image.Symbols);
            if (referenceImages is not null)
            {
                var dependencies = referenceImages.Select(bytes => System.Reflection.Assembly.Load(bytes))
                    .ToDictionary(dependency => dependency.GetName().Name!, StringComparer.Ordinal);
                externalResolver = (_, request) => dependencies.GetValueOrDefault(new AssemblyName(request.Name).Name!);
                AppDomain.CurrentDomain.AssemblyResolve += externalResolver;
            }
            if (privateAuthority)
            {
                Assert.True(includeAuthority);
                privateContext = new AssemblyLoadContext("S9PrivateAuthority" + Guid.NewGuid().ToString("N"), isCollectible: true);
                privateContext.Resolving += (_, name) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(loaded => loaded.GetName().Name == name.Name);
                var privateCore = privateContext.LoadFromAssemblyPath(typeof(JsonHashInput).Assembly.Location);
                Assembly = privateContext.LoadFromAssemblyPath(ImagePath);
                Assemblies = [Assembly, privateCore];
            }
            else
            {
                Assembly = System.Reflection.Assembly.Load(image.Image);
                Assemblies = includeAuthority ? [Assembly, typeof(JsonHashInput).Assembly] : [Assembly];
            }
            Graph = JsonHashReferenceGraph.Scan(Assemblies);
            Assert.Empty(Graph.Errors);
            Entry = Graph.Definitions.Values.Single(d => d.Type == "Recipe" && d.Name == "Hash").Identity;
            Maps = new() { [Assembly.ManifestModule.ModuleVersionId] = JsonHashSourceDocuments.Read(ImagePath, Root) };
            if (includeAuthority)
            {
                var configuration = typeof(JsonHashRecipeReceiptTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
                var project = Path.Join(RepoPaths.RepoRoot, "essentials/src/Serialization/Core/Elsa.Serialization.Core.csproj");
                var inputs = new EvaluatedHashInputs(configuration);
                ExternalSources = inputs.PackageSources(project);
                Maps.Add(typeof(JsonHashInput).Module.ModuleVersionId, JsonHashSourceDocuments.Read(typeof(JsonHashInput).Assembly.Location,
                    RepoPaths.RepoRoot, new Dictionary<string, string> { ["/_/"] = RepoPaths.RepoRoot }, inputs.CompileSources(project), [inputs.IntermediateRoot(project)]));
            }
            Assert.Empty(Graph.Errors); Assert.All(Maps.Values, map => Assert.Empty(map.Errors));
        }
        internal JsonHashRecipeReceipt.Receipt Receipt() => JsonHashRecipeReceipt.Create(Entry, Graph, Assemblies, Maps, ExternalSources);
        public void Dispose()
        {
            if (externalResolver is not null) AppDomain.CurrentDomain.AssemblyResolve -= externalResolver;
            privateContext?.Unload();
            var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Root);
            Assert.StartsWith(expected, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(target, recursive: true);
        }
    }
}
