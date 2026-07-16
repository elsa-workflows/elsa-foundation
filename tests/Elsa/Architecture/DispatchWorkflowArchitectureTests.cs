using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class DispatchWorkflowArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string DispatchRoot = Path.Combine(RepoRoot, "src", "Elsa", "Activities", "DispatchWorkflow");

    [Fact]
    public void DispatchWorkflow_runtime_does_not_reference_design()
    {
        var project = XDocument.Load(Path.Combine(DispatchRoot, "Runtime", "Elsa.Activities.DispatchWorkflow.Runtime.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();

        Assert.DoesNotContain(references, reference =>
            reference.Contains("Activities/Design", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Activities\\Design", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Workflows/Design", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Workflows\\Design", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DispatchWorkflow_modules_do_not_take_transport_distributed_or_studio_dependencies()
    {
        var forbidden = new[]
        {
            "MassTransit",
            "Elsa.Workflows.Runtime.Distributed",
            "Elsa.Studio",
            "elsa-foundation-studio"
        };
        var violations = Directory.EnumerateFiles(DispatchRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(file => forbidden
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Path.GetRelativePath(RepoRoot, file)} -> {token}"))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DispatchWorkflow_slice_does_not_implement_definition_or_later_lifecycle_concerns()
    {
        var forbiddenTypeNames = new Regex(
            @"\b(?:class|interface|record|enum)\s+(?:WorkflowDefinitionActivity|DispatchWorkflow(?:Completion|Cancellation|Redrive|TestScope|Placement)\w*)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var violations = Directory.EnumerateFiles(DispatchRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => (File: file, Match: forbiddenTypeNames.Match(File.ReadAllText(file))))
            .Where(result => result.Match.Success)
            .Select(result => $"{Path.GetRelativePath(RepoRoot, result.File)} -> {result.Match.Value}")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
