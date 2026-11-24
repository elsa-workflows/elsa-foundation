using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Elsa.Architecture.Tests;

/// <summary>One immutable validation snapshot, scoped to actual owning projects and their partial/linked declarations.</summary>
internal sealed class JsonBaselineConstructionContexts
{
    internal sealed record SourceView(SyntaxNode Root, IReadOnlyList<SyntaxNode> StorageRoots, Func<SemanticModel> Model);
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> owners;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<SyntaxTree>> sources;
    private readonly IReadOnlyDictionary<string, string> images;
    private readonly IReadOnlyDictionary<string, Assembly> bindings;
    private readonly IReadOnlyDictionary<string, string?> imageHashes;
    private readonly IReadOnlyList<SyntaxNode> snapshotRoots;
    private readonly IReadOnlyDictionary<string, CSharpParseOptions> parseOptions;
    private readonly Dictionary<string, IReadOnlyList<SyntaxNode>> parsedRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<SyntaxNode>> storageViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpCompilation> compilations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReferenceImage> referenceImages = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string>? frameworkPaths;
    private readonly Dictionary<string, MetadataReference> frameworkReferences = new(StringComparer.Ordinal);
    private readonly string configuration;

    internal static IReadOnlyList<SyntaxNode> ReadSources(string repoRoot, IReadOnlyList<ShippingHashAssemblies.Project> projects)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot)) + Path.DirectorySeparatorChar;
        var paths = ModuleRoots.Resolve(repoRoot, [.. ModuleRoots.Production, "Studio/src", "extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path) && ModuleRoots.IsNotTestFile(repoRoot, path))
            .Select(Path.GetFullPath).ToHashSet(comparer);
        foreach (var project in projects)
        {
            JsonHashSourceDocuments? externalWitness = null;
            foreach (var input in project.CompileSources ?? AuthoredDocuments(project.AssemblyPath, repoRoot))
            {
                var path = Path.GetFullPath(input);
                if (!path.EndsWith(".cs", comparison) || RepoPaths.IsBuildOutput(repoRoot, path)) continue;
                // A linked authored input is production code regardless of the directory it lives
                // in. Unknown external inputs are coverage failures, not silent root exemptions.
                if (!File.Exists(path)) throw new InvalidOperationException("A JSON baseline compile source is missing: " + path);
                if (!path.StartsWith(prefix, comparison))
                {
                    var compileSources = project.CompileSources;
                    var packageSources = project.PackageSources;
                    if (compileSources is null || packageSources is null || !compileSources.Contains(path) || !packageSources.ContainsKey(path))
                        throw new InvalidOperationException("An unknown JSON baseline compile source is outside the repository: " + path);
                    // Package labels identify exact evaluated inputs; they never authenticate
                    // bytes. Reuse the shipping PE/PDB/source-checksum witness before admission.
                    externalWitness ??= JsonHashSourceDocuments.Read(project.AssemblyPath, repoRoot,
                        new Dictionary<string, string> { ["/_/"] = repoRoot },
                        externalCompileSources: packageSources.Keys.Where(compileSources.Contains).ToHashSet(comparer),
                        generatedSourceRoots: project.IntermediateRoot is { } intermediate ? [intermediate] : null);
                    if (externalWitness.Errors.Count != 0)
                        throw new InvalidOperationException("Cannot authenticate JSON baseline package sources for " + project.Path + ":\n" + string.Join('\n', externalWitness.Errors));
                    if (!externalWitness.Documents.TryGetValue(path, out var document) || document.Origin != "external-compile")
                        throw new InvalidOperationException("A JSON baseline package source has no authenticated compiled document: " + path);
                }
                paths.Add(path);
            }
        }
        return paths.Order(StringComparer.Ordinal)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot()).ToArray();
    }

    internal JsonBaselineConstructionContexts(IReadOnlyList<SyntaxNode> roots, IReadOnlyList<ShippingHashAssemblies.Project> projects,
        string configuration, IReadOnlyList<Assembly>? shippingAssemblies = null)
    {
        this.configuration = configuration;
        snapshotRoots = roots;
        images = projects.ToDictionary(project => Path.GetFullPath(project.Path), project => project.AssemblyPath, StringComparer.Ordinal);
        imageHashes = projects.ToDictionary(project => Path.GetFullPath(project.Path), project => project.ImageSha256, StringComparer.Ordinal);
        var shippingByName = shippingAssemblies?.GroupBy(assembly => assembly.GetName().Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        bindings = shippingAssemblies is null ? new Dictionary<string, Assembly>() : projects.ToDictionary(project => Path.GetFullPath(project.Path), project =>
        {
            var candidates = shippingByName!.GetValueOrDefault(Path.GetFileNameWithoutExtension(project.AssemblyPath)) ?? [];
            var matches = candidates.Where(assembly => ShippingHashAssemblies.MatchesImage(assembly, project.AssemblyPath)).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidOperationException("A JSON construction owner has no unique authenticated shipping assembly: " + project.Path);
        }, StringComparer.Ordinal);
        parseOptions = projects.ToDictionary(project => Path.GetFullPath(project.Path), project => ReadParseOptions(project.AssemblyPath), StringComparer.Ordinal);
        var folders = projects.Select(project => (Project: Path.GetFullPath(project.Path), Folder: Path.GetDirectoryName(Path.GetFullPath(project.Path))! + Path.DirectorySeparatorChar))
            .OrderByDescending(project => project.Folder.Length).ToArray();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var treesByPath = roots.ToDictionary(root => Path.GetFullPath(root.SyntaxTree.FilePath), root => root.SyntaxTree, pathComparer);
        var projectSources = folders.ToDictionary(project => project.Project, _ => new HashSet<SyntaxTree>(), StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var owner = folders.FirstOrDefault(project => Path.GetFullPath(root.SyntaxTree.FilePath).StartsWith(project.Folder, comparison)).Project;
            if (owner is not null) projectSources[owner].Add(root.SyntaxTree);
        }
        foreach (var project in projects)
            // Evaluated compile membership is authoritative when supplied. Otherwise portable PDB
            // documents retain linked authored files outside the owning project's directory.
            foreach (var path in project.CompileSources ?? AuthoredDocuments(project.AssemblyPath, RepoPaths.RepoRoot))
                if (treesByPath.TryGetValue(Path.GetFullPath(path), out var tree)) projectSources[Path.GetFullPath(project.Path)].Add(tree);
        sources = projectSources.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<SyntaxTree>)pair.Value.ToArray(), StringComparer.Ordinal);
        owners = projectSources.SelectMany(pair => pair.Value.Select(tree => (Path: Path.GetFullPath(tree.FilePath), Project: pair.Key)))
            .GroupBy(pair => pair.Path, pathComparer).ToDictionary(group => group.Key,
                group => (IReadOnlyList<string>)group.Select(pair => pair.Project).Order(StringComparer.Ordinal).ToArray(), pathComparer);
        foreach (var path in treesByPath.Keys)
            if (!owners.ContainsKey(path)) throw new InvalidOperationException("A JSON construction source has no owning project: " + path);
    }

    internal IReadOnlyList<SourceView> Views(SyntaxNode root)
    {
        if (!owners.TryGetValue(Path.GetFullPath(root.SyntaxTree.FilePath), out var projects))
            throw new InvalidOperationException("JSON construction target source is outside the project inventory: " + root.SyntaxTree.FilePath);
        return projects.Select(project =>
        {
            var projectRoots = ParsedRoots(project);
            var original = projectRoots.Single(node => SamePath(node.SyntaxTree.FilePath, root.SyntaxTree.FilePath));
            var active = original.ToFullString() == root.ToFullString() ? original
                : CSharpSyntaxTree.ParseText(root.ToFullString(), parseOptions[project], path: root.SyntaxTree.FilePath).GetRoot();
            if (!storageViews.TryGetValue(project, out var storage))
            {
                var paths = projectRoots.Select(node => Path.GetFullPath(node.SyntaxTree.FilePath))
                    .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                storage = projectRoots.Concat(snapshotRoots.Where(node => !paths.Contains(Path.GetFullPath(node.SyntaxTree.FilePath)))).ToArray();
                storageViews.Add(project, storage);
            }
            return new SourceView(active, storage, () => Model(active, project));
        }).ToArray();
    }

    private IReadOnlyList<SyntaxNode> ParsedRoots(string project)
    {
        if (!parsedRoots.TryGetValue(project, out var roots))
        {
            roots = sources[project].Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(), parseOptions[project], path: tree.FilePath).GetRoot()).ToArray();
            parsedRoots.Add(project, roots);
        }
        return roots;
    }

    private SemanticModel Model(SyntaxNode root, string project)
    {
        if (!compilations.TryGetValue(project, out var compilation))
        {
            var image = Path.GetFullPath(images[project]);
            if (!File.Exists(image)) throw new InvalidOperationException("Build the JSON construction source's project first: " + image);
            var trees = ParsedRoots(project).Select(node => node.SyntaxTree).ToList();
            var framework = Path.GetFileName(Path.GetDirectoryName(image));
            var globals = Path.Join(Path.GetDirectoryName(project), "obj", configuration, framework,
                Path.GetFileNameWithoutExtension(project) + ".GlobalUsings.g.cs");
            // This is the SDK's actual per-project generated source, not a guessed ambient import set.
            if (File.Exists(globals)) trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(globals), parseOptions[project], path: globals));
            compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(image), trees,
                References(image, bindings.GetValueOrDefault(project), imageHashes[project]),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Enable));
            compilations.Add(project, compilation);
        }
        if (!compilation.SyntaxTrees.Contains(root.SyntaxTree))
        {
            // A causal probe may replace a source in this snapshot. Never lend its old symbols
            // to the new text, and never mutate the snapshot used by another source's scan.
            var previous = compilation.SyntaxTrees.Single(tree => SamePath(tree.FilePath, root.SyntaxTree.FilePath));
            compilation = compilation.ReplaceSyntaxTree(previous, root.SyntaxTree);
        }
        return compilation.GetSemanticModel(root.SyntaxTree, ignoreAccessibility: true);
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static CSharpParseOptions ReadParseOptions(string image)
    {
        var symbols = Path.ChangeExtension(image, ".pdb");
        if (!File.Exists(image) || !File.Exists(symbols))
            throw new InvalidOperationException("JSON construction parse facts need the actual built PE and portable PDB: " + image);
        using var peStream = File.OpenRead(image);
        using var pe = new PEReader(peStream);
        using var stream = File.OpenRead(symbols);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (entries.Length != 1 || reader.DebugMetadataHeader is not { } header || header.Id.Length != 20)
            throw new InvalidDataException("JSON construction parse facts have no unique portable PE/PDB identity: " + image);
        var identity = header.Id.AsSpan();
        var codeView = pe.ReadCodeViewDebugDirectoryData(entries[0]);
        if (entries[0].MinorVersion != 0x504d || codeView.Age != 1 || codeView.Guid != new Guid(identity[..16]) ||
            entries[0].Stamp != BinaryPrimitives.ReadUInt32LittleEndian(identity[16..]))
            throw new InvalidDataException("JSON construction parse facts belong to a different PE/PDB pair: " + image);
        var checksumEntries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
        if (checksumEntries.Length != 1) throw new InvalidDataException("JSON construction parse facts need the PE's PDB checksum: " + image);
        var checksum = pe.ReadPdbChecksumDebugDirectoryData(checksumEntries[0]);
        var bytes = File.ReadAllBytes(symbols);
        if (header.IdStartOffset < 0 || header.IdStartOffset > bytes.Length - header.Id.Length)
            throw new InvalidDataException("Invalid JSON construction PDB content identity offset: " + image);
        // The portable-PDB checksum clears its content ID, which the PE identity above binds.
        bytes.AsSpan(header.IdStartOffset, header.Id.Length).Clear();
        var actual = checksum.AlgorithmName switch
        {
            "SHA256" => SHA256.HashData(bytes), "SHA384" => SHA384.HashData(bytes),
            "SHA512" => SHA512.HashData(bytes), "SHA1" => SHA1.HashData(bytes), _ => null
        };
        if (actual is null || !actual.AsSpan().SequenceEqual(checksum.Checksum.AsSpan()))
            throw new InvalidDataException("JSON construction parse facts have a changed or unsupported PDB checksum: " + image);
        // Portable PDB CompilationOptions: UTF-8 NUL-terminated name/value pairs.
        // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#compilation-options-c-and-vb-compilers
        var kind = new Guid("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");
        var records = reader.CustomDebugInformation.Select(reader.GetCustomDebugInformation)
            .Where(record => record.Parent.Kind == HandleKind.ModuleDefinition && reader.GetGuid(record.Kind) == kind).ToArray();
        if (records.Length != 1) throw new InvalidDataException("JSON construction parse facts need exactly one CompilationOptions record: " + image);
        var values = new UTF8Encoding(false, true).GetString(reader.GetBlobBytes(records[0].Value)).Split('\0');
        if (values.Length < 3 || values[^1] != "" || values.Length % 2 != 1)
            throw new InvalidDataException("Malformed JSON construction CompilationOptions pairs: " + image);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length - 1; index += 2)
            if (values[index].Length == 0 || !options.TryAdd(values[index], values[index + 1]))
                throw new InvalidDataException("Duplicate or empty JSON construction CompilationOptions key: " + image);
        if (options.GetValueOrDefault("language") != "C#" || !LanguageVersionFacts.TryParse(options.GetValueOrDefault("language-version"), out var language))
            throw new InvalidDataException("Unsupported or missing JSON construction C# parse facts: " + image);
        // Roslyn omits define when the compilation's symbol set is empty; that is distinct
        // from missing CompilationOptions, which must never silently become default parsing.
        var defines = options.GetValueOrDefault("define", "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (defines.Any(symbol => !SyntaxFacts.IsValidIdentifier(symbol)))
            throw new InvalidDataException("Invalid JSON construction preprocessor symbols: " + image);
        return new CSharpParseOptions(language, preprocessorSymbols: defines);
    }

    private static IReadOnlySet<string> AuthoredDocuments(string image, string repoRoot)
    {
        var symbols = Path.ChangeExtension(image, ".pdb");
        if (!File.Exists(symbols)) throw new InvalidOperationException("JSON construction linked-source inventory needs the built portable PDB: " + symbols);
        using var stream = File.OpenRead(symbols);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        return reader.Documents.Select(handle => reader.GetString(reader.GetDocument(handle).Name)).Select(path =>
            path.StartsWith("/_/", StringComparison.Ordinal) ? Path.Join(repoRoot, path[3..]) : path)
            .Where(Path.IsPathRooted).Select(Path.GetFullPath).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private IReadOnlyList<MetadataReference> References(string image, Assembly? owner, string? ownerSha256)
    {
        var platform = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted
            ? trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) : [typeof(object).Assembly.Location])
            .Where(File.Exists).ToArray();
        var frameworkFolders = new[] { typeof(object).Assembly.Location, typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location }
            .Select(path => Path.GetDirectoryName(path)!).ToHashSet(StringComparer.Ordinal);
        frameworkPaths ??= PlatformReferences(platform.Where(path => frameworkFolders.Contains(Path.GetDirectoryName(path)!)));
        foreach (var (name, path) in frameworkPaths)
            if (!frameworkReferences.ContainsKey(name)) frameworkReferences.Add(name, MetadataReference.CreateFromFile(path));
        var references = new Dictionary<string, MetadataReference>(frameworkReferences, StringComparer.Ordinal);
        var selectedImages = new Dictionary<string, ReferenceImage>(StringComparer.Ordinal);
        var resolver = new AssemblyDependencyResolver(image);
        var pending = new Queue<(ReferenceImage Image, Assembly? Binding)>();
        var inspected = new HashSet<(string Path, AssemblyLoadContext? Context)>();
        var output = Path.GetDirectoryName(image)!;
        var own = Reference(image, owner);
        if (ownerSha256 is not null && own.Sha256 != ownerSha256)
            throw new InvalidOperationException("A JSON construction owner differs from its shipping image witness: " + image);
        pending.Enqueue((own, owner));
        while (pending.TryDequeue(out var current))
        {
            if (!inspected.Add((current.Image.Path, current.Binding is { } loaded ? AssemblyLoadContext.GetLoadContext(loaded) : null))) continue;
            foreach (var requested in current.Image.Dependencies)
            {
                var name = requested.Name!;
                if (name == own.Name.Name || frameworkReferences.ContainsKey(name)) continue;
                Assembly? binding = null;
                string? path;
                if (current.Binding is not null)
                {
                    // Studio has a private ABI. Use the actual requesting assembly's context,
                    // including shared backend contracts, rather than the testhost's closure.
                    try
                    {
                        var context = AssemblyLoadContext.GetLoadContext(current.Binding)
                            ?? throw new InvalidOperationException("A JSON construction owner has no load context: " + current.Binding.FullName);
                        binding = context.LoadFromAssemblyName(requested);
                    }
                    catch (Exception error) when (error is FileNotFoundException or FileLoadException or BadImageFormatException)
                    {
                        throw new InvalidOperationException($"JSON construction context cannot bind {requested.FullName} for {current.Image.Path}.", error);
                    }
                    if (binding.IsDynamic) throw new InvalidOperationException("A JSON construction dependency has no file witness: " + binding.FullName);
                    path = binding.Location;
                }
                else
                {
                    // Synthetic controls have no shipping ALC. Their explicit output/deps
                    // closure remains supported, without borrowing arbitrary host assemblies.
                    var copy = Path.Join(output, name + ".dll");
                    path = File.Exists(copy) ? copy : resolver.ResolveAssemblyToPath(requested);
                }
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException($"JSON construction target dependency is unresolved for {image}: {requested.FullName}");
                var selected = Reference(path, binding, requested);
                if (selectedImages.TryGetValue(name, out var previous))
                {
                    if (!SameIdentity(previous.Name, selected.Name) || previous.Sha256 != selected.Sha256)
                        throw new InvalidOperationException($"JSON construction dependency has incompatible context images for {name}: {previous.Path}, {selected.Path}");
                }
                else
                {
                    selectedImages.Add(name, selected);
                    references.Add(name, selected.Metadata);
                }
                pending.Enqueue((selected, binding));
            }
        }
        return references.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value).ToArray();
    }

    private sealed record ReferenceImage(string Path, AssemblyName Name, Guid Mvid, string Sha256,
        MetadataReference Metadata, IReadOnlyList<AssemblyName> Dependencies);

    private ReferenceImage Reference(string path, Assembly? binding, AssemblyName? requested = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("A JSON construction binding has no file witness: " + (binding?.FullName ?? path));
        path = Path.GetFullPath(path);
        if (!referenceImages.TryGetValue(path, out var selected))
        {
            var bytes = File.ReadAllBytes(path);
            using var pe = new PEReader(new MemoryStream(bytes, writable: false));
            var reader = pe.GetMetadataReader();
            var definition = reader.GetAssemblyDefinition();
            var name = Identity(reader, definition.Name, definition.Version, definition.Culture, definition.PublicKey, publicKey: true);
            selected = new(path, name, reader.GetGuid(reader.GetModuleDefinition().Mvid), Convert.ToHexStringLower(SHA256.HashData(bytes)),
                MetadataReference.CreateFromImage(bytes, filePath: path), reader.AssemblyReferences.Select(handle =>
                {
                    var dependency = reader.GetAssemblyReference(handle);
                    return Identity(reader, dependency.Name, dependency.Version, dependency.Culture, dependency.PublicKeyOrToken,
                        (dependency.Flags & AssemblyFlags.PublicKey) != 0);
                }).ToArray());
            referenceImages.Add(path, selected);
        }
        if (requested is not null && !SameIdentity(selected.Name, requested))
            throw new InvalidOperationException($"JSON construction dependency identity differs: requested {requested.FullName}, selected {selected.Name.FullName} at {path}.");
        if (binding is not null && (binding.IsDynamic || !SameIdentity(selected.Name, binding.GetName()) || selected.Mvid != binding.ManifestModule.ModuleVersionId))
            throw new InvalidOperationException("A JSON construction metadata file differs from its actual loaded binding: " + path);
        return selected;
    }

    private static AssemblyName Identity(MetadataReader reader, StringHandle name, Version version, StringHandle culture,
        BlobHandle key, bool publicKey)
    {
        var identity = new AssemblyName(reader.GetString(name)) { Version = version, CultureName = culture.IsNil ? "" : reader.GetString(culture) };
        var bytes = reader.GetBlobBytes(key);
        if (publicKey && bytes.Length != 0) identity.SetPublicKey(bytes);
        else identity.SetPublicKeyToken(bytes);
        return identity;
    }

    private static bool SameIdentity(AssemblyName left, AssemblyName right) => left.Name == right.Name && left.Version == right.Version &&
        string.Equals(left.CultureName ?? "", right.CultureName ?? "", StringComparison.OrdinalIgnoreCase) && left.ContentType == right.ContentType &&
        (left.GetPublicKeyToken() ?? []).AsSpan().SequenceEqual(right.GetPublicKeyToken() ?? []);

    internal static Dictionary<string, string> PlatformReferences(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(comparer))
        {
            var name = AssemblyName.GetAssemblyName(path).Name!;
            if (!references.TryAdd(name, path))
                throw new InvalidDataException($"JSON construction platform identity '{name}' has conflicting image paths: {references[name]}, {path}");
        }
        return references;
    }
}
