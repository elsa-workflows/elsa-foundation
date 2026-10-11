using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed class DispatchWorkflowArchitectureTests
{
    private static readonly string DispatchRoot = Path.Combine(RepoRoot, "src", "essentials", "Activities", "DispatchWorkflow");

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
}
