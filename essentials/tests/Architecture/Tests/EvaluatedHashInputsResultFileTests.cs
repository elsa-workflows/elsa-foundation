using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class EvaluatedHashInputsResultFileTests
{
    [Fact]
    public void Actual_SDK_warnings_do_not_corrupt_evaluated_compile_or_reference_images()
    {
        var root = Path.Join(Path.GetTempPath(), "elsa-sdk-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Join(root, "Warning.csproj");
            var source = Path.Join(root, "Input.cs");
            var config = Path.Join(root, "NuGet.Config");
            var warningWitness = Path.Join(root, "warning-target-ran.txt");
            File.WriteAllText(config, "<configuration><packageSources><clear /></packageSources></configuration>");
            File.WriteAllText(source, "public class Input { public string Name { get; set; } = string.Empty; }");
            File.WriteAllText(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <Target Name="EmitEvidenceWarning" BeforeTargets="FindReferenceAssembliesForReferences">
                    <Warning Code="S9WARN001" Text="Synthetic SDK warning: this is not structured JSON { warning }." />
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/warning-target-ran.txt" Lines="S9WARN001" Overwrite="true" />
                  </Target>
                </Project>
                """);
            var (code, output) = ChildProcess.Run("dotnet",
                ["restore", project, "--configfile", config, "-p:NuGetAudit=false", "-m:1", "-nr:false"],
                workingDirectory: root, timeout: TimeSpan.FromMinutes(5));
            Assert.True(code == 0, output);

            var evaluated = new EvaluatedHashInputs("Debug");
            var inputs = evaluated.Read(project);
            Assert.Equal("S9WARN001", File.ReadAllText(warningWitness).Trim());
            Assert.Contains(Path.GetFullPath(source), inputs);
            Assert.Contains(Path.GetFullPath(source), evaluated.CompileSources(project));
            var references = evaluated.CompileReferences(project);
            Assert.NotEmpty(references);
            Assert.Contains("System.Runtime", references.Keys);
            Assert.All(references.Values, reference =>
            {
                Assert.True(File.Exists(reference.Path), reference.Path);
                Assert.NotEqual(Guid.Empty, reference.Mvid);
                Assert.Equal(64, reference.Sha256.Length);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"Items\":{}}\nSDK warning after JSON")]
    [InlineData("{\"Items\":{}} {}")]
    public void Missing_or_invalid_structured_results_are_refused_without_a_console_fallback(string? content)
    {
        var result = Path.Join(Path.GetTempPath(), "elsa-sdk-result-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            if (content is null)
                Assert.Throws<InvalidOperationException>(() => EvaluatedHashInputs.ReadResultFile(result, "fixture.csproj"));
            else
            {
                File.WriteAllText(result, content);
                Assert.ThrowsAny<JsonException>(() => EvaluatedHashInputs.ReadResultFile(result, "fixture.csproj"));
            }
        }
        finally { if (File.Exists(result)) File.Delete(result); }
    }
}
