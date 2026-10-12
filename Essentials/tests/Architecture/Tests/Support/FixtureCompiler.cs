using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Compiles a small C# fixture into an in-memory library image against the test host's platform assemblies, for
/// guards that load or inspect compiled code they plant themselves.
/// </summary>
internal static class FixtureCompiler
{
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
        OptimizationLevel optimization = OptimizationLevel.Release)
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
}
