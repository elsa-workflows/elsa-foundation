using Elsa.Workflows.Design.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Elsa.Workflows.Design.CodeGeneration.Tests;

public sealed class ActivityCallGeneratorTests
{
    [Fact]
    public void Generator_emits_its_bootstrap_marker_without_compilation_errors()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorConsumer",
            syntaxTrees: [CSharpSyntaxTree.ParseText("namespace GeneratorConsumer; internal sealed class Workflow;")],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ActivityCallGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(updatedCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var generatorResult = Assert.Single(driver.GetRunResult().Results);
        var generatedSource = Assert.Single(generatorResult.GeneratedSources);
        Assert.Equal(ActivityCallGenerator.GeneratedHintName, generatedSource.HintName);
        Assert.Contains("ElsaActivityCallGeneratorMarker", generatedSource.SourceText.ToString());
    }
}
