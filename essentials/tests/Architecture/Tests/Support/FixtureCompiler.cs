using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Compiles a small C# fixture into an in-memory library image against the test host's platform assemblies, for
/// guards that load or inspect compiled code they plant themselves.
/// </summary>
internal static class FixtureCompiler
{
    internal sealed record PortableImage(byte[] Image, byte[] Symbols);
    private static readonly Lazy<(HashSet<string> Paths, MetadataReference[] References)> Platform = new(() =>
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted
                ? trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                : [typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location])
            .Where(File.Exists)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (paths, [.. paths.Select(path => MetadataReference.CreateFromFile(path))]);
    });

    /// <summary>
    /// The image of <paramref name="source"/> compiled as library <paramref name="assemblyName"/>, referencing the
    /// platform assemblies and <paramref name="references"/>. Throws, listing the errors, when it does not compile.
    /// </summary>
    internal static byte[] Compile(
        string assemblyName,
        string source,
        IEnumerable<Assembly>? references = null,
        OptimizationLevel optimization = OptimizationLevel.Release,
        bool allowUnsafe = false)
    {
        var (platformPaths, platformReferences) = Platform.Value;
        var extra = (references ?? [])
            .Select(assembly => assembly.Location)
            .Where(path => !platformPaths.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            [.. platformReferences, .. extra],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe,
                optimizationLevel: optimization,
                nullableContextOptions: NullableContextOptions.Enable));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"The fixture {assemblyName} failed to compile:{Environment.NewLine}{errors}");
        }

        return output.ToArray();
    }

    internal static PortableImage CompilePortable(string assemblyName, params string[] sources)
        => CompilePortable(assemblyName, embedSources: false, sources);

    internal static PortableImage CompilePortable(string assemblyName, bool embedSources, params string[] sources)
        => CompilePortableCore(assemblyName, embedSources, [], sources);

    internal static PortableImage CompilePortable(string assemblyName, IEnumerable<Assembly> references, params string[] sources)
        => CompilePortableCore(assemblyName, false, references, sources);

    internal static PortableImage CompilePortableConfigured(string assemblyName, IEnumerable<Assembly> references, IEnumerable<string> symbols, params string[] sources)
        => CompilePortableCore(assemblyName, false, references, sources, symbols);

    internal static PortableImage CompilePortableWithReferenceImages(string assemblyName, IEnumerable<byte[]> referenceImages, params string[] sources)
        => CompilePortableCore(assemblyName, false, [], sources, imageReferences: referenceImages);

    private static PortableImage CompilePortableCore(string assemblyName, bool embedSources, IEnumerable<Assembly> references, string[] sources,
        IEnumerable<string>? preprocessorSymbols = null, IEnumerable<byte[]>? imageReferences = null)
    {
        var trees = sources.Select(path =>
        {
            using var stream = File.OpenRead(path);
            return CSharpSyntaxTree.ParseText(SourceText.From(stream, Encoding.UTF8, SourceHashAlgorithm.Sha256, canBeEmbedded: embedSources),
                new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols), path);
        }).ToArray();
        var (platformPaths, platformReferences) = Platform.Value;
        var extra = references.Select(assembly => assembly.Location).Where(path => !platformPaths.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(assemblyName, trees,
            [.. platformReferences, .. extra, .. (imageReferences ?? []).Select(image => MetadataReference.CreateFromImage(image))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        using var image = new MemoryStream();
        using var symbols = new MemoryStream();
        var result = compilation.Emit(image, pdbStream: symbols,
            embeddedTexts: embedSources ? trees.Select(tree => EmbeddedText.FromSource(tree.FilePath, tree.GetText())) : [],
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: assemblyName + ".pdb",
                pdbChecksumAlgorithm: System.Security.Cryptography.HashAlgorithmName.SHA256));
        if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return new(image.ToArray(), symbols.ToArray());
    }
}
