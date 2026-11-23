using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashSourceDocumentsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void Authenticated_generated_source_can_be_read_from_raw_or_compressed_PDB_content(int padding)
    {
        using var fixture = new Sources();
        var path = Path.Join(fixture.Root, "obj", "generated", "Recipe.g.cs");
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; } //" + new string('x', padding), true, path);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        var map = JsonHashSourceDocuments.Read(image, fixture.Root, generatedSourceRoots: [Path.Join(fixture.Root, "obj")]);
        Assert.Empty(map.Errors);
        var document = Assert.Single(map.Documents.Values);
        Assert.Equal("embedded-generated", document.Origin);
        Assert.Equal(bytes, document.EmbeddedSource);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), document.Sha256);
        Assert.Contains(map.Methods.Values, method => method.Documents.Contains("obj/generated/Recipe.g.cs"));
    }

    [Fact]
    public void Embedded_source_does_not_waive_a_missing_authored_file_or_a_changed_generated_file()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }", true);
        File.Delete(Path.Join(fixture.Root, "Recipe.cs"));
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, generatedSourceRoots: [Path.Join(fixture.Root, "obj")]).Errors,
            error => error.StartsWith("Missing PDB source document", StringComparison.Ordinal));
        var generated = Path.Join(fixture.Root, "obj", "Recipe.g.cs");
        image = fixture.Build("public static class Recipe { public static int Run() => 1; }", true, generated);
        File.WriteAllText(generated, "public static class Recipe { public static int Run() => 2; }");
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, generatedSourceRoots: [Path.Join(fixture.Root, "obj")]).Errors,
            error => error.StartsWith("PDB source checksum mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void External_sources_require_an_exact_evaluated_compile_item_and_the_matching_source_checksum()
    {
        using var fixture = new Sources();
        using var dependency = new Sources();
        var external = Path.Join(dependency.Root, "Recipe.cs");
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }", sourcePath: external);
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root).Errors, error => error.StartsWith("Unmapped", StringComparison.Ordinal));
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, externalCompileSources: new HashSet<string> { external + ".other" }).Errors,
            error => error.StartsWith("Unmapped", StringComparison.Ordinal));
        var inputs = new HashSet<string> { external };
        var map = JsonHashSourceDocuments.Read(image, fixture.Root, externalCompileSources: inputs);
        Assert.Empty(map.Errors);
        var document = Assert.Single(map.Documents.Values);
        Assert.Equal("external-compile", document.Origin);
        Assert.Equal(external, document.Path);
        Assert.Null(document.EmbeddedSource);
        File.WriteAllText(external, "public static class Recipe { public static int Run() => 2; }");
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, externalCompileSources: inputs).Errors,
            error => error.StartsWith("PDB source checksum mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void A_compile_item_cannot_reclassify_an_ambiguous_checkout_mapping_as_external()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }");
        var path = Path.Join(fixture.Root, "Recipe.cs");
        var maps = new Dictionary<string, string> { [fixture.Root] = Path.Join(fixture.Root, "other") };
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, maps, new HashSet<string> { path }).Errors,
            error => error.StartsWith("Unmapped or ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void An_outside_compile_item_cannot_erase_ambiguous_explicit_checkout_mappings()
    {
        using var fixture = new Sources();
        using var dependency = new Sources();
        var external = Path.Join(dependency.Root, "shared", "Recipe.cs");
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }", sourcePath: external);
        var maps = new Dictionary<string, string>
        {
            [dependency.Root] = Path.Join(fixture.Root, "a"),
            [Path.Join(dependency.Root, "shared")] = Path.Join(fixture.Root, "b")
        };
        Assert.Null(JsonHashSourceDocuments.Resolve(external, fixture.Root, maps));
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root, maps, new HashSet<string> { external }).Errors,
            error => error.StartsWith("Unmapped or ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void Matching_symbols_bind_verified_source_and_async_kickoff()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static async System.Threading.Tasks.Task<int> Run() { await System.Threading.Tasks.Task.Yield(); return 1; } }");
        var map = JsonHashSourceDocuments.Read(image, fixture.Root);
        Assert.True(map.Errors.Count == 0, string.Join('\n', map.Errors));
        Assert.Equal(System.Reflection.Assembly.Load(File.ReadAllBytes(image)).ManifestModule.ModuleVersionId, map.ImageMvid);
        Assert.Contains("Recipe.cs", map.Documents.Keys);
        Assert.Contains(map.Methods.Values, method => method.KickoffToken is not null && method.Documents.Contains("Recipe.cs"));
    }

    [Fact]
    public void A_source_edit_with_preserved_timestamp_cannot_borrow_old_symbols()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }");
        var path = Path.Join(fixture.Root, "Recipe.cs");
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "public static class Recipe { public static int Run() => 2; }");
        File.SetLastWriteTimeUtc(path, timestamp);
        var map = JsonHashSourceDocuments.Read(image, fixture.Root);
        Assert.Contains(map.Errors, error => error.StartsWith("PDB source checksum mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_or_other_builds_PDB_is_not_source_evidence()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }");
        var symbols = Path.ChangeExtension(image, ".pdb");
        var otherPath = Path.Join(fixture.Root, "Other.cs");
        File.WriteAllText(otherPath, "public static class OtherRecipe { public static int Run() => 2; }");
        var other = FixtureCompiler.CompilePortable("Other", otherPath);
        File.WriteAllBytes(symbols, other.Symbols);
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root).Errors, error => error.StartsWith("PE/PDB debug identity mismatch", StringComparison.Ordinal));
        File.Delete(symbols);
        Assert.Contains(JsonHashSourceDocuments.Read(image, fixture.Root).Errors, error => error.StartsWith("Missing portable PDB", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_path_maps_reject_suffix_guessing_escapes_and_ambiguity()
    {
        using var fixture = new Sources();
        var path = Path.Join(fixture.Root, "Recipe.cs");
        var maps = new Dictionary<string, string> { ["/_/"] = fixture.Root };
        Assert.Equal(path, JsonHashSourceDocuments.Resolve("/_/Recipe.cs", fixture.Root, maps));
        Assert.Null(JsonHashSourceDocuments.Resolve("/some-other-checkout/Recipe.cs", fixture.Root, maps));
        Assert.Null(JsonHashSourceDocuments.Resolve("/_/../escaped.cs", fixture.Root, maps));
        maps["/_/nested/"] = fixture.Root;
        Assert.Null(JsonHashSourceDocuments.Resolve("/_/nested/Recipe.cs", fixture.Root, maps));
    }

    [Fact]
    public void PDB_content_changes_cannot_reuse_an_unchanged_debug_identity()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }");
        using (var stream = new FileStream(Path.ChangeExtension(image, ".pdb"), FileMode.Append)) stream.WriteByte(1);
        var map = JsonHashSourceDocuments.Read(image, fixture.Root);
        Assert.Contains(map.Errors, error => error.StartsWith("PDB checksum mismatch", StringComparison.Ordinal));
        Assert.Empty(map.Documents);
    }

    [Fact]
    public void Roots_with_a_trailing_separator_keep_direct_and_mapped_sources()
    {
        using var fixture = new Sources();
        var image = fixture.Build("public static class Recipe { public static int Run() => 1; }");
        var root = fixture.Root + Path.DirectorySeparatorChar;
        var path = Path.Join(fixture.Root, "Recipe.cs");
        Assert.Equal(path, JsonHashSourceDocuments.Resolve(path, root, new Dictionary<string, string>()));
        Assert.Equal(path, JsonHashSourceDocuments.Resolve("/_/Recipe.cs", root, new Dictionary<string, string> { ["/_/"] = root }));
        Assert.Empty(JsonHashSourceDocuments.Read(image, root).Errors);
    }

    [SourceLinkFact]
    public void Source_links_cannot_import_bytes_from_outside_the_owned_root()
    {
        using var fixture = new Sources();
        using var outside = new Sources();
        outside.Build("public static class Recipe { public static int Run() => 1; }");
        var link = Path.Join(fixture.Root, "escape");
        Directory.CreateSymbolicLink(link, outside.Root);
        try
        {
            Assert.Null(JsonHashSourceDocuments.Resolve(Path.Join(link, "Recipe.cs"), fixture.Root, new Dictionary<string, string>()));
            Assert.Null(JsonHashSourceDocuments.Resolve("/_/Recipe.cs", fixture.Root, new Dictionary<string, string> { ["/_/"] = link }));
            var indirect = Path.Join(fixture.Root, "Recipe.cs");
            File.CreateSymbolicLink(indirect, Path.Join(link, "Recipe.cs"));
            Assert.Null(JsonHashSourceDocuments.Resolve(indirect, fixture.Root, new Dictionary<string, string>()));
            var compiled = FixtureCompiler.CompilePortable("S9.LinkedSource", indirect);
            var image = Path.Join(fixture.Root, "S9.LinkedSource.dll");
            File.WriteAllBytes(image, compiled.Image);
            File.WriteAllBytes(Path.ChangeExtension(image, ".pdb"), compiled.Symbols);
            var sources = JsonHashSourceDocuments.Read(image, fixture.Root);
            Assert.Contains(sources.Errors, error => error.StartsWith("Unmapped or ambiguous PDB source document", StringComparison.Ordinal));
            Assert.Empty(sources.Documents);
            // Owned source paths deliberately refuse all links, including links within the tree.
            var owned = Path.Join(fixture.Root, "owned");
            Directory.CreateDirectory(owned);
            Directory.CreateSymbolicLink(Path.Join(fixture.Root, "alias"), owned);
            Assert.Null(JsonHashSourceDocuments.Resolve(
                Path.Join(fixture.Root, "alias", "Recipe.cs"), fixture.Root, new Dictionary<string, string>()));
            File.Delete(indirect);
            Directory.CreateDirectory(Path.Join(outside.Root, "subdir"));
            Directory.CreateSymbolicLink(Path.Join(fixture.Root, "directoryAlias"), Path.Join(outside.Root, "subdir"));
            File.CreateSymbolicLink(indirect, Path.Join(fixture.Root, "directoryAlias", "..", "Recipe.cs"));
            Assert.Null(JsonHashSourceDocuments.Resolve(indirect, fixture.Root, new Dictionary<string, string>()));
            Assert.Null(JsonHashSourceDocuments.Resolve(Path.Join(fixture.Root, "directoryAlias", "..", "Recipe.cs"), fixture.Root, new Dictionary<string, string>()));
        }
        finally { Directory.Delete(link); }
    }

    [SourceLinkFact]
    public void Cyclic_source_links_fail_with_a_bounded_diagnostic()
    {
        using var fixture = new Sources();
        var first = Path.Join(fixture.Root, "first.cs");
        var second = Path.Join(fixture.Root, "second.cs");
        File.CreateSymbolicLink(first, second);
        File.CreateSymbolicLink(second, first);
        Assert.Null(JsonHashSourceDocuments.Resolve(first, fixture.Root, new Dictionary<string, string>()));
    }

    private sealed class SourceLinkFactAttribute : FactAttribute
    {
        public SourceLinkFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Link creation needs a Windows host privilege; execute this control on the permitted Linux path.";
        }
    }

    [WindowsPathFact]
    public void Drive_relative_and_current_drive_paths_do_not_borrow_the_ambient_directory()
    {
        using var fixture = new Sources();
        var path = Path.Join(fixture.Root, "Recipe.cs");
        var drive = Path.GetPathRoot(path)!;
        var driveRelative = drive.TrimEnd(Path.DirectorySeparatorChar) + Path.GetRelativePath(Environment.CurrentDirectory, path);
        Assert.Null(JsonHashSourceDocuments.Resolve(driveRelative, fixture.Root, new Dictionary<string, string>()));
        Assert.Null(JsonHashSourceDocuments.Resolve(Path.DirectorySeparatorChar + Path.GetRelativePath(drive, path), fixture.Root, new Dictionary<string, string>()));
    }

    private sealed class WindowsPathFactAttribute : FactAttribute
    {
        public WindowsPathFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows drive-relative path semantics require the Windows control.";
        }
    }

    private sealed class Sources : IDisposable
    {
        internal string Root { get; } = Path.Join(RepoPaths.RepoRoot, ".agent-prefs", "s9-pdb-source-" + Guid.NewGuid().ToString("N"));
        internal Sources() => Directory.CreateDirectory(Root);
        internal string Build(string source, bool embedSources = false, string? sourcePath = null)
        {
            var path = sourcePath ?? Path.Join(Root, "Recipe.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
            var compiled = FixtureCompiler.CompilePortable("S9.PdbRecipe", embedSources, path);
            var image = Path.Join(Root, "S9.PdbRecipe.dll");
            File.WriteAllBytes(image, compiled.Image);
            File.WriteAllBytes(Path.ChangeExtension(image, ".pdb"), compiled.Symbols);
            return image;
        }
        public void Dispose()
        {
            var scope = Path.GetFullPath(Path.Join(RepoPaths.RepoRoot, ".agent-prefs")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(scope, Path.GetFullPath(Root), StringComparison.Ordinal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
