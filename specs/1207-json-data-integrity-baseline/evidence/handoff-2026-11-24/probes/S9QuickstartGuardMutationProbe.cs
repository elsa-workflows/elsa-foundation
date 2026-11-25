using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Serialization.Core;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Elsa.Architecture.Tests;

// Temporary manual quickstart step 4. Its intentional failure must name the actual mutated source.
public sealed class S9QuickstartGuardMutationProbe
{
    [Fact]
    public void An_actual_source_DictionaryKeyPolicy_mutation_is_refused_and_restored()
    {
        var repo = RepoPaths.RepoRoot;
        var configuration = GetType().Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Assert.Single(ShippingHashAssemblies.Discover(repo, configuration),
            project => Path.GetFileName(project.Path) == "Elsa.Serialization.Core.csproj");
        var evaluator = new EvaluatedHashInputs(configuration);
        _ = evaluator.Read(project.Path);
        project = project with { CompileSources = evaluator.CompileSources(project.Path),
            IntermediateRoot = evaluator.IntermediateRoot(project.Path), PackageSources = evaluator.PackageSources(project.Path),
            CompileReferences = evaluator.CompileReferences(project.Path),
            ImageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(project.AssemblyPath))) };
        var roots = project.CompileSources!.Where(path => !RepoPaths.IsBuildOutput(repo, path))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot()).ToArray();
        var contexts = new JsonBaselineConstructionContexts(roots, [project], configuration, [typeof(JsonBaseline).Assembly]);
        var path = Path.Join(repo, "essentials/src/Serialization/Core/JsonBaseline.cs");
        var original = File.ReadAllBytes(path);
        var text = File.ReadAllText(path);
        const string before = "DictionaryKeyPolicy = null,";
        const string after = "DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,";
        Assert.Equal(1, text.Split(before, StringSplitOptions.None).Length - 1);
        var mutated = text.Replace(before, after, StringComparison.Ordinal);
        var evidence = Environment.GetEnvironmentVariable("S9_BACKEND_QUICKSTART_ROOT")!;
        Directory.CreateDirectory(evidence);
        try
        {
            File.WriteAllText(path, mutated);
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
            var finding = Assert.Single(JsonBaselineSourceScan.Scan(root, roots, contexts)
                .Where(finding => finding.Setting == "DictionaryKeyPolicy"));
            var relative = Path.GetRelativePath(repo, path).Replace('\\', '/');
            var register = JsonBaselineSourceScan.ReadRegister(File.ReadAllText(Path.Join(repo, "docs/serialization.md")));
            var accounted = JsonBaselineSourceScan.IsAccounted(relative, finding, new HashSet<string>(), register);
            File.WriteAllText(Path.Join(evidence, "source-mutation-observation.json"), JsonSerializer.Serialize(new
            {
                file = relative, finding.Line, finding.Setting, finding.Signature, accounted,
                originalSha256 = Convert.ToHexStringLower(SHA256.HashData(original))
            }));
            Assert.True(accounted, $"Unregistered JSON options: {relative}:{finding.Line} | {finding.Setting} | {finding.Signature}");
        }
        finally
        {
            Assert.Equal(mutated, File.ReadAllText(path));
            File.WriteAllBytes(path, original);
            Assert.Equal(original, File.ReadAllBytes(path));
            File.WriteAllText(Path.Join(evidence, "source-mutation-restoration.json"), "{\"restored\":true}");
        }
    }
}
