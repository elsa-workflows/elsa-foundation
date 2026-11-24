using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonBaselineConstructionContextTests
{
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
        private readonly string[] defines;
        internal string RepositoryRoot => root;
        internal string Source { get; }
        internal string Linked { get; }
        private string Project { get; }
        internal string Image { get; }
        private string? Globals { get; }

        internal Sources(string source, string linked, bool external = false, string? globals = null, string[]? symbols = null)
        {
            this.external = external;
            defines = symbols ?? [];
            Source = Path.Join(root, "essentials", "src", "Owner", "Source.cs");
            Linked = Path.Join(root, "tools", "Linked", "Contract.cs");
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
            var dependency = external ? FixtureCompiler.CompilePortable("ExternalTargets", Linked) : null;
            var inputs = Globals is { } globals ? new[] { Source, Linked, globals } : new[] { Source, Linked };
            var compiled = dependency is null ? FixtureCompiler.CompilePortableConfigured("Owner", [], defines, inputs)
                : FixtureCompiler.CompilePortableWithReferenceImages("Owner", [dependency.Image], Source);
            File.WriteAllBytes(Image, compiled.Image);
            File.WriteAllBytes(Path.ChangeExtension(Image, ".pdb"), compiled.Symbols);
            if (dependency is not null)
            {
                File.WriteAllBytes(Path.Join(Path.GetDirectoryName(Image), "ExternalTargets.dll"), dependency.Image);
                File.WriteAllBytes(Path.Join(Path.GetDirectoryName(Image), "ExternalTargets.pdb"), dependency.Symbols);
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
            Directory.Delete(root, recursive: true);
        }
    }
}
