using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonBaselineConstructionContextTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SDK_selected_compile_images_allow_only_evidenced_unification_and_ignore_a_foreign_runtime_ABI(bool unify)
    {
        var name = "S9SdkTargets_" + Guid.NewGuid().ToString("N");
        var firstParameter = unify ? "object business" : "System.Text.Json.JsonSerializerOptions options";
        using var fixture = new Sources("using External; public class C { public static object Make() => new Holder(new()); }",
            "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] namespace External; public class Holder { public Holder(" + firstParameter + ") {} }",
            external: true, dependencyName: name);
        var first = File.ReadAllBytes(fixture.DependencyImage);
        var secondSource = Path.Join(fixture.RepositoryRoot, "SdkSecond.cs");
        File.WriteAllText(secondSource, "[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")] namespace External; public class Holder { public Holder(" +
            (unify ? "System.Text.Json.JsonSerializerOptions options" : "object business") + ") {} }");
        var second = FixtureCompiler.CompilePortable(name, secondSource);
        var secondPath = Path.Join(fixture.RepositoryRoot, "SdkSecond.dll"); File.WriteAllBytes(secondPath, second.Image);
        var selected = unify ? secondPath : fixture.DependencyImage;
        var references = CompilerReferencePaths().Append(selected).ToList();
        if (unify)
        {
            // A transitive binary retains its original 1.0 request. The root's SDK selected
            // 2.0; choosing the old dependency would incorrectly bind new() to object.
            var middleSource = Path.Join(fixture.RepositoryRoot, "Middle.cs");
            File.WriteAllText(middleSource, "public class Middle { public External.Holder Value => new External.Holder(new object()); }");
            var middle = FixtureCompiler.CompilePortableWithReferenceImages("SdkMiddle", [first], middleSource);
            var middlePath = Path.Join(fixture.RepositoryRoot, "SdkMiddle.dll"); File.WriteAllBytes(middlePath, middle.Image);
            references.Add(middlePath);
            File.WriteAllText(fixture.Source, "using External; public class C { public static object Make() => new Holder(new()); public static object OldEdge() => new Middle(); }");
            var root = FixtureCompiler.CompilePortableWithReferenceImages("Owner", [second.Image, middle.Image], fixture.Source);
            File.WriteAllBytes(fixture.Image, root.Image); File.WriteAllBytes(Path.ChangeExtension(fixture.Image, ".pdb"), root.Symbols);
        }
        var binding = new FixtureBindingContext(name, secondPath, fromStream: false);
        try
        {
            var owner = binding.LoadFromAssemblyPath(fixture.Image);
            _ = binding.LoadFromAssemblyPath(secondPath); // A higher runtime version is never compiler evidence.
            var roots = fixture.Roots();
            var record = fixture.Record(roots) with { CompileReferences = SdkReferenceSelection(references) };
            Assert.Equal(unify ? new Version(2, 0, 0, 0) : new Version(1, 0, 0, 0), new AssemblyName(record.CompileReferences[name].Identity).Version);
            var contexts = new JsonBaselineConstructionContexts(roots, [record], "Debug", [owner]);
            Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, contexts)).Setting);
            Assert.Equal(0, binding.Resolutions);
        }
        finally { binding.Unload(); }
    }

    [Fact]
    public void SDK_reference_bytes_cannot_change_after_the_evaluated_selection_witness()
    {
        var name = "S9SdkWitness_" + Guid.NewGuid().ToString("N");
        using var fixture = new Sources("using External; public class C { public static object Make() => new Holder(new()); }",
            "namespace External; public class Holder { public Holder(System.Text.Json.JsonSerializerOptions options) {} }", external: true, dependencyName: name);
        var roots = fixture.Roots();
        var record = fixture.Record(roots) with { CompileReferences = SdkReferenceSelection(CompilerReferencePaths().Append(fixture.DependencyImage)) };
        File.WriteAllText(fixture.Linked, "namespace External; public class Holder { public Holder(object business) {} }");
        File.WriteAllBytes(fixture.DependencyImage, FixtureCompiler.CompilePortable(name, fixture.Linked).Image);
        var contexts = new JsonBaselineConstructionContexts(roots, [record], "Debug");
        Assert.Throws<InvalidOperationException>(() => JsonBaselineSourceScan.Scan(roots[0], roots, contexts));
    }

    [Fact]
    public void SDK_reference_aliases_are_used_by_the_actual_construction_model()
    {
        var name = "S9SdkAlias_" + Guid.NewGuid().ToString("N");
        using var fixture = new Sources("using External; public class C { public static object Make() => new Holder(new()); }",
            "namespace External; public class Holder { public Holder(System.Text.Json.JsonSerializerOptions options) {} }", external: true, dependencyName: name);
        var roots = fixture.Roots();
        var record = fixture.Record(roots) with { CompileReferences = SdkReferenceSelection(CompilerReferencePaths().Append(fixture.DependencyImage), fixture.DependencyImage) };
        var contexts = new JsonBaselineConstructionContexts(roots, [record], "Debug");
        var changed = CSharpSyntaxTree.ParseText("extern alias owned; public class C { public static object Make() => new owned::External.Holder(new()); }", path: fixture.Source).GetRoot();
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(changed, roots, contexts)).Setting);
    }

    private static string[] CompilerReferencePaths() =>
        [typeof(object).Assembly.Location, typeof(System.Text.Json.JsonSerializerOptions).Assembly.Location,
            Path.Join(Path.GetDirectoryName(typeof(object).Assembly.Location), "System.Runtime.dll")];

    private static IReadOnlyDictionary<string, SdkCompileReferences.Asset> SdkReferenceSelection(IEnumerable<string> paths, string? aliasPath = null)
    {
        using var evaluated = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            Items = new { ReferencePathWithRefAssemblies = paths.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new { FullPath = path, Aliases = path == aliasPath ? "owned" : "", EmbedInteropTypes = "false" }).ToArray() }
        }));
        return new SdkCompileReferences().Read(evaluated.RootElement);
    }

    [Theory]
    [InlineData("owner-context")]
    [InlineData("wrong-version")]
    [InlineData("unresolved-context")]
    [InlineData("unresolved-standalone")]
    [InlineData("missing-file-witness")]
    public void Noncopied_library_dependencies_use_the_owning_binding_and_refuse_wrong_or_unwitnessed_images(string mode)
    {
        var dependencyName = "S9ConstructionContextTargets_" + Guid.NewGuid().ToString("N");
        using var fixture = new Sources("using External; public class C { public static object Make() => new Holder(new()); }", """
            using System.Text.Json;
            [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
            namespace External;
            public class Holder { public Holder(JsonSerializerOptions options) {} }
            """, external: true, dependencyName: dependencyName);
        var directory = Path.Join(fixture.RepositoryRoot, "context-dependencies");
        Directory.CreateDirectory(directory);
        var selected = Path.Join(directory, dependencyName + ".dll");
        File.Move(fixture.DependencyImage, selected);
        Assert.False(File.Exists(fixture.DependencyImage)); // The original failing class-library shape.
        if (mode == "wrong-version")
        {
            File.WriteAllText(fixture.Linked, """
                [assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
                namespace External;
                public class Holder { public Holder(object business) {} }
                """);
            File.WriteAllBytes(selected, FixtureCompiler.CompilePortable(dependencyName, fixture.Linked).Image);
        }
        var binding = new FixtureBindingContext(dependencyName, mode == "unresolved-context" ? null : selected,
            fromStream: mode == "missing-file-witness");
        try
        {
            var owner = binding.LoadFromAssemblyPath(fixture.Image);
            if (mode == "wrong-version") _ = binding.LoadFromAssemblyPath(selected);
            var roots = fixture.Roots();
            var contexts = new JsonBaselineConstructionContexts(roots, [fixture.Record(roots)], "Debug",
                mode == "unresolved-standalone" ? null : [owner]);
            if (mode == "owner-context")
            {
                Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, contexts)).Setting);
                Assert.True(binding.Resolutions > 0);
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => JsonBaselineSourceScan.Scan(roots[0], roots, contexts));
                if (mode == "wrong-version") Assert.Contains("2.0.0.0", error.Message);
            }
        }
        finally { binding.Unload(); }
    }

    private sealed class FixtureBindingContext(string dependencyName, string? dependencyPath, bool fromStream)
        : AssemblyLoadContext("S9 construction binding " + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        internal int Resolutions { get; private set; }
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name != dependencyName || dependencyPath is null) return null;
            Resolutions++;
            if (!fromStream) return LoadFromAssemblyPath(dependencyPath);
            using var stream = File.OpenRead(dependencyPath);
            return LoadFromStream(stream);
        }
    }

    [Fact]
    public void Repeated_physical_TPA_entries_are_one_reference_and_conflicting_images_are_never_arbitrarily_selected()
    {
        var core = typeof(object).Assembly.Location;
        Assert.Equal(core, Assert.Single(JsonBaselineConstructionContexts.PlatformReferences([core, core])).Value);
        using var first = new Sources("public class First {}", "public class FirstLinked {}");
        using var second = new Sources("public class Second {}", "public class SecondLinked {}");
        Assert.Throws<InvalidDataException>(() => JsonBaselineConstructionContexts.PlatformReferences([first.Image, second.Image]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_inventory_discovers_linked_inputs_before_scanning_and_rechecks_their_mutated_contracts(bool evaluated)
    {
        using var fixture = new Sources("public partial class C { public static object Make() => new C(new()); }",
            "public partial class C { public C(object value) {} }", symbols: ["ELSA_DASHBOARD_QUALIFICATION"]);
        // The production entry point finds directory roots itself. The caller supplies no linked
        // SyntaxNode: only the project's actual compile membership or portable PDB can find it.
        var before = fixture.Inventory(evaluated);
        Assert.Equal(2, before.Count);
        Assert.Contains(before, root => root.SyntaxTree.FilePath == fixture.Linked);
        var original = fixture.Contexts(before, evaluated);
        Assert.Empty(before.SelectMany(root => JsonBaselineSourceScan.Scan(root, before, original)));

        File.WriteAllText(fixture.Linked, """
            using System.Text.Json;
            public partial class C {
            #if ELSA_DASHBOARD_QUALIFICATION
                public C(JsonSerializerOptions options) {}
                public static JsonSerializerOptions Settings() => new();
                public static object Serialize() => JsonSerializer.SerializeToElement(1);
            #else
                public C(object value) {}
            #endif
            }
            """);
        fixture.Build();
        var after = fixture.Inventory(evaluated);
        var current = fixture.Contexts(after, evaluated);
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(after.Single(root => root.SyntaxTree.FilePath == fixture.Source), after, current)).Setting);
        var linked = JsonBaselineSourceScan.Scan(after.Single(root => root.SyntaxTree.FilePath == fixture.Linked), after, current);
        Assert.Equal(new[] { "construction", "optionless" }, linked.Select(finding => finding.Setting));
        Assert.All(linked, finding => Assert.False(JsonBaselineSourceScan.IsAccounted("tools/Linked/Contract.cs", finding, new HashSet<string>(), [])));
        Assert.Empty(before.SelectMany(root => JsonBaselineSourceScan.Scan(root, before, original)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_missing_authored_link_is_a_production_inventory_coverage_failure(bool evaluated)
    {
        using var fixture = new Sources("public class C {}", "public class Linked {}");
        File.Delete(fixture.Linked);
        Assert.Throws<InvalidOperationException>(() => fixture.Inventory(evaluated));
    }

    [Fact]
    public void An_external_authored_compile_source_cannot_be_silently_exempted_by_the_directory_sweep()
    {
        using var fixture = new Sources("public class C {}", "public class Linked {}");
        using var other = new Sources("public class Other {}", "public class OtherLinked {}");
        var record = fixture.Record(fixture.Roots()) with { CompileSources = new HashSet<string> { fixture.Source, other.Linked } };
        Assert.Throws<InvalidOperationException>(() => JsonBaselineConstructionContexts.ReadSources(fixture.RepositoryRoot, [record]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_exact_package_compile_source_is_scanned_only_with_matching_build_authenticated_bytes(bool mutate)
    {
        using var package = new Sources("public class PackageOwner {}", "public class OriginalPackageInput {}");
        using var fixture = new Sources("public class C {}", "using System.Text.Json; public class PackageInput { public static object Write() => JsonSerializer.SerializeToElement(1); }",
            linkedPath: package.Linked);
        var record = fixture.Record(fixture.Roots());
        var assets = Path.Join(fixture.RepositoryRoot, "project.assets.json");
        File.WriteAllText(assets, System.Text.Json.JsonSerializer.Serialize(new
        {
            packageFolders = new Dictionary<string, object> { [package.RepositoryRoot] = new { } },
            libraries = new Dictionary<string, object> { ["Example/1.2.3"] = new { type = "package", path = "tools", files = new[] { "Linked/Contract.cs" } } }
        }));
        record = record with { PackageSources = JsonHashExternalSources.Read(assets, record.CompileSources!) };
        Assert.Equal("nuget:example/1.2.3/Linked/Contract.cs", Assert.Single(record.PackageSources!).Value);
        if (mutate)
        {
            File.AppendAllText(fixture.Linked, "\n// Bytes changed after the owning PE/PDB was built.\n");
            Assert.Throws<InvalidOperationException>(() => JsonBaselineConstructionContexts.ReadSources(fixture.RepositoryRoot, [record]));
            return;
        }
        var roots = JsonBaselineConstructionContexts.ReadSources(fixture.RepositoryRoot, [record]);
        var linked = Assert.Single(roots.Where(root => root.SyntaxTree.FilePath == fixture.Linked));
        var contexts = new JsonBaselineConstructionContexts(roots, [record], "Debug");
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(linked, roots, contexts));
        Assert.Equal("optionless", finding.Setting);
        Assert.False(JsonBaselineSourceScan.IsAccounted(Assert.Single(record.PackageSources!).Value, finding, new HashSet<string>(), []));
    }

    [Fact]
    public void Linked_partial_constructor_declarations_supply_the_actual_target_outside_the_project_folder()
    {
        using var fixture = new Sources("""
            using System.Text.Json;
            namespace Scoped;
            public partial class C { public static object Make() => new C(new()); }
            """, """
            using System.Text.Json;
            namespace Scoped;
            public partial class C { public C(JsonSerializerOptions options) {} }
            """);
        var roots = fixture.Roots();
        var contexts = fixture.Contexts(roots, evaluated: false);
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, contexts)).Setting);
    }

    [Fact]
    public void A_fresh_two_file_snapshot_detects_a_constructor_target_change_without_reusing_old_partial_symbols()
    {
        using var fixture = new Sources("""
            using System.Text.Json;
            namespace Scoped;
            public partial class C { public static object Make() => new C(new()); }
            """, """
            using System.Text.Json;
            namespace Scoped;
            public partial class C { public C(object value) {} }
            """);
        var before = fixture.Roots();
        var original = fixture.Contexts(before);
        Assert.Empty(JsonBaselineSourceScan.Scan(before[0], before, original));
        File.WriteAllText(fixture.Linked, File.ReadAllText(fixture.Linked).Replace("object value", "JsonSerializerOptions options", StringComparison.Ordinal));
        fixture.Build();
        var after = fixture.Roots();
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(after[0], after, fixture.Contexts(after))).Setting);
        // A context belongs to its immutable input snapshot, not a global source or symbol cache.
        Assert.Empty(JsonBaselineSourceScan.Scan(before[0], before, original));
    }

    [Fact]
    public void A_probe_replacing_the_current_tree_cannot_borrow_that_trees_cached_target_facts()
    {
        using var fixture = new Sources("using System.Text.Json; public class C { public static object Make() => new(); }", "public class Other {} ");
        var roots = fixture.Roots();
        var contexts = fixture.Contexts(roots);
        Assert.Empty(JsonBaselineSourceScan.Scan(roots[0], roots, contexts));
        var changed = CSharpSyntaxTree.ParseText(File.ReadAllText(fixture.Source).Replace("object Make", "JsonSerializerOptions Make", StringComparison.Ordinal), path: fixture.Source).GetRoot();
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(changed, roots, contexts)).Setting);
        Assert.Empty(JsonBaselineSourceScan.Scan(roots[0], roots, contexts));
    }

    [Fact]
    public void Compiled_external_constructor_and_property_targets_use_the_owners_dependency_metadata()
    {
        using var fixture = new Sources("""
            using External;
            public class C { public static object Make() => new Holder(new()) { Options = new() }; }
            """, """
            using System.Text.Json;
            namespace External;
            public class Holder { public Holder(JsonSerializerOptions options) {} public JsonSerializerOptions Options { get; set; } }
            """, external: true);
        var roots = fixture.Roots();
        Assert.Equal(2, JsonBaselineSourceScan.Scan(roots[0], roots, fixture.Contexts(roots)).Count(finding => finding.Setting == "construction"));
    }

    [Fact]
    public void Same_named_partial_types_in_other_projects_cannot_supply_constructor_parameters()
    {
        const string source = "using System.Text.Json; public partial class C { public static object Make() => new C(new()); }";
        using var options = new Sources(source, "using System.Text.Json; public partial class C { public C(JsonSerializerOptions options) {} }");
        using var business = new Sources(source, "public partial class C { public C(object value) {} }");
        var first = options.Roots();
        var second = business.Roots();
        var roots = first.Concat(second).ToArray();
        var contexts = new JsonBaselineConstructionContexts(roots, [options.Record(first), business.Record(second)], "Debug");
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(first[0], roots, contexts)).Setting);
        Assert.Empty(JsonBaselineSourceScan.Scan(second[0], roots, contexts));
    }

    [Fact]
    public void Only_the_actual_generated_project_global_imports_supply_implicit_names()
    {
        using var fixture = new Sources("public class C { public static List<JsonSerializerOptions> Make() => [new()]; }", "public class Other {}",
            globals: "global using System.Collections.Generic; global using System.Text.Json;");
        var roots = fixture.Roots();
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, fixture.Contexts(roots))).Setting);
    }

    [Fact]
    public void Unresolved_production_targets_are_coverage_failures_that_no_register_entry_can_waive()
    {
        using var fixture = new Sources("public class C { public static object Make() => new Holder(new()); }", "public class Holder { public Holder(object value) {} }");
        var roots = fixture.Roots().Take(1).ToArray(); // Deliberately lose the actual constructor declaration.
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, fixture.Contexts(roots)));
        Assert.Equal("construction-unresolved", finding.Setting);
        var register = new[] { new JsonBaselineSourceScan.Registration("owner.cs", new HashSet<string> { JsonBaselineSourceScan.RegistrationKey(finding) }, "Cannot approve missing coverage.", "control") };
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), register));
    }

    [Fact]
    public void A_resolved_generic_wrapper_cannot_hide_an_unresolved_production_construction_target()
    {
        using var fixture = new Sources("using System.Collections.Generic; public class C { public static List<LinkedType> Make() => new(); }",
            "public class LinkedType {}");
        var roots = fixture.Roots().Take(1).ToArray(); // The wrapper resolves; its actual argument declaration is missing.
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, fixture.Contexts(roots)));
        Assert.Equal("construction-unresolved", finding.Setting);
        var register = new[] { new JsonBaselineSourceScan.Registration("owner.cs", new HashSet<string> { JsonBaselineSourceScan.RegistrationKey(finding) }, "A wrapper cannot approve missing type facts.", "control") };
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), register));
    }

    [Fact]
    public void An_unresolved_nullable_production_receiver_is_nonwaivable_coverage_failure()
    {
        using var fixture = new Sources("using System.Text.Json; public class C { public static void M(JsonSerializerOptions? wire) { wire!.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase; } }",
            "public class LinkedType {}");
        var roots = fixture.Roots();
        var contexts = fixture.Contexts(roots);
        var changed = CSharpSyntaxTree.ParseText(File.ReadAllText(fixture.Source).Replace("using System.Text.Json;", "", StringComparison.Ordinal), path: fixture.Source).GetRoot();
        var finding = Assert.Single(JsonBaselineSourceScan.Scan(changed, roots, contexts).Where(finding => finding.Setting == "construction-unresolved"));
        var register = new[] { new JsonBaselineSourceScan.Registration("owner.cs", new HashSet<string> { JsonBaselineSourceScan.RegistrationKey(finding) }, "A nullable wrapper cannot approve missing receiver facts.", "control") };
        Assert.False(JsonBaselineSourceScan.IsAccounted("owner.cs", finding, new HashSet<string>(), register));
    }

    [Fact]
    public void The_actual_built_framework_and_module_symbols_expose_conditional_options_constructions()
    {
        const string source = """
            using System.Text.Json;
            public class C {
            #if NET10_0_OR_GREATER && ELSA_DASHBOARD_QUALIFICATION
                public static JsonSerializerOptions Make() => new();
            #else
                public static object Make() => new();
            #endif
            }
            """;
        using var enabled = new Sources(source, "public class Other {}", symbols: ["NET10_0_OR_GREATER", "ELSA_DASHBOARD_QUALIFICATION"]);
        using var disabled = new Sources(source, "public class Other {}", symbols: ["NET10_0_OR_GREATER"]);
        var first = enabled.Roots();
        var second = disabled.Roots();
        Assert.Empty(JsonBaselineSourceScan.Scan(first[0])); // Causal control: default parsing hid the shipping branch.
        var roots = first.Concat(second).ToArray();
        var contexts = new JsonBaselineConstructionContexts(roots, [enabled.Record(first), disabled.Record(second)], "Debug");
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(first[0], roots, contexts)).Setting);
        Assert.Empty(JsonBaselineSourceScan.Scan(second[0], roots, contexts));
        enabled.BorrowSymbols(disabled);
        Assert.Throws<InvalidDataException>(() => enabled.Contexts(first));
    }

    [Fact]
    public void A_conditional_foreign_partial_member_uses_the_same_actual_project_symbols_as_its_caller()
    {
        using var fixture = new Sources("public partial class C { public static object Make() => new C(new()); }", """
            using System.Text.Json;
            public partial class C {
            #if ELSA_DASHBOARD_QUALIFICATION
                public C(JsonSerializerOptions options) {}
            #else
                public C(object business) {}
            #endif
            }
            """, symbols: ["ELSA_DASHBOARD_QUALIFICATION"]);
        var roots = fixture.Roots();
        Assert.Equal("construction", Assert.Single(JsonBaselineSourceScan.Scan(roots[0], roots, fixture.Contexts(roots))).Setting);
    }

    [Fact]
    public void Missing_compiled_parse_facts_cannot_silently_select_default_preprocessor_branches()
    {
        using var fixture = new Sources("public class C { public static object Make() => new(); }", "public class Other {}");
        var roots = fixture.Roots();
        fixture.RemoveSymbols();
        Assert.Throws<InvalidOperationException>(() => fixture.Contexts(roots));
    }

    private sealed class Sources : IDisposable
    {
        private readonly string root = Path.Join(Path.GetTempPath(), "elsa-json-construction-targets-" + Guid.NewGuid().ToString("N"));
        private readonly bool external;
        private readonly string dependencyName;
        private readonly string[] defines;
        internal string RepositoryRoot => root;
        internal string Source { get; }
        internal string Linked { get; }
        private string Project { get; }
        internal string Image { get; }
        internal string DependencyImage => Path.Join(Path.GetDirectoryName(Image), dependencyName + ".dll");
        private string? Globals { get; }

        internal Sources(string source, string linked, bool external = false, string? globals = null, string[]? symbols = null, string? linkedPath = null,
            string dependencyName = "ExternalTargets")
        {
            this.external = external;
            this.dependencyName = dependencyName;
            defines = symbols ?? [];
            Source = Path.Join(root, "essentials", "src", "Owner", "Source.cs");
            Linked = linkedPath ?? Path.Join(root, "tools", "Linked", "Contract.cs");
            Project = Path.Join(Path.GetDirectoryName(Source), "Owner.csproj");
            Image = Path.Join(Path.GetDirectoryName(Source), "bin", "Debug", "net10.0", "Owner.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Linked)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Image)!);
            File.WriteAllText(Project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Source, source);
            File.WriteAllText(Linked, linked);
            if (globals is not null)
            {
                Globals = Path.Join(Path.GetDirectoryName(Source), "obj", "Debug", "net10.0", "Owner.GlobalUsings.g.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(Globals)!);
                File.WriteAllText(Globals, globals);
            }
            Build();
        }

        internal void Build()
        {
            var dependency = external ? FixtureCompiler.CompilePortable(dependencyName, Linked) : null;
            var inputs = Globals is { } globals ? new[] { Source, Linked, globals } : new[] { Source, Linked };
            var compiled = dependency is null ? FixtureCompiler.CompilePortableConfigured("Owner", [], defines, inputs)
                : FixtureCompiler.CompilePortableWithReferenceImages("Owner", [dependency.Image], Source);
            File.WriteAllBytes(Image, compiled.Image);
            File.WriteAllBytes(Path.ChangeExtension(Image, ".pdb"), compiled.Symbols);
            if (dependency is not null)
            {
                File.WriteAllBytes(DependencyImage, dependency.Image);
                File.WriteAllBytes(Path.ChangeExtension(DependencyImage, ".pdb"), dependency.Symbols);
            }
        }

        internal IReadOnlyList<SyntaxNode> Roots() => (external ? new[] { Source } : new[] { Source, Linked })
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot()).ToArray();

        internal IReadOnlyList<SyntaxNode> Inventory(bool evaluated) => JsonBaselineConstructionContexts.ReadSources(root,
            [new(Project, Image, CompileSources: evaluated ? new HashSet<string> { Source, Linked } : null)]);

        internal void RemoveSymbols() => File.Delete(Path.ChangeExtension(Image, ".pdb"));
        internal void BorrowSymbols(Sources other) => File.Copy(Path.ChangeExtension(other.Image, ".pdb"), Path.ChangeExtension(Image, ".pdb"), overwrite: true);

        internal ShippingHashAssemblies.Project Record(IReadOnlyList<SyntaxNode> roots, bool evaluated = true) =>
            new(Project, Image, CompileSources: evaluated ? roots.Select(node => node.SyntaxTree.FilePath).ToHashSet(StringComparer.Ordinal) : null);

        internal JsonBaselineConstructionContexts Contexts(IReadOnlyList<SyntaxNode> roots, bool evaluated = true) => new(roots,
            [Record(roots, evaluated)], "Debug");

        public void Dispose()
        {
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(prefix, Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            // Windows keeps an image loaded into the test process locked; the temp tree is left for the OS then.
            try { Directory.Delete(root, recursive: true); }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { }
            catch (IOException) when (OperatingSystem.IsWindows()) { }
        }
    }
}
