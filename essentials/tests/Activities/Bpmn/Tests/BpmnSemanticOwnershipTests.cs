using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>The host retains authored DTOs and compatibility/adapter code, while semantic policy belongs to the released library.</summary>
public sealed class BpmnSemanticOwnershipTests
{
    [Fact]
    public void Foundation_defines_no_type_with_a_public_Semantics_simple_name()
    {
        var semanticsNames = typeof(global::Bpmn.Semantics.BpmnInterpreter).Assembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("Bpmn.Semantics", StringComparison.Ordinal) == true)
            .Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(semanticsNames);
        var collisions = typeof(Activities.BpmnProcess).Assembly.GetTypes()
            .Where(type => semanticsNames.Contains(type.Name)).Select(type => type.FullName).Order().ToArray();
        Assert.Empty(collisions);
    }

    [Fact]
    public void Deleted_semantic_sources_types_and_host_pruning_do_not_return()
    {
        var package = PackageDirectory();
        Assert.True(Directory.Exists(package), "The guard must inspect the real Foundation source package.");
        foreach (var path in DeletedSources)
            Assert.False(File.Exists(Path.Combine(package, path)), $"Deleted semantic source returned: {path}");
        var types = typeof(Activities.BpmnProcess).Assembly.GetTypes();
        var deletedNames = DeletedSources.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(types.Where(type => deletedNames.Contains(type.Name)).Select(type => type.FullName));
        Assert.DoesNotContain(types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
            method => method.Name.Contains("PruneForPersistence", StringComparison.Ordinal));
    }

    private static string PackageDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath("../../../../src/Activities/Bpmn/Elsa.Activities.Bpmn", Path.GetDirectoryName(sourcePath)!);

    private static readonly string[] DeletedSources =
    [
        "Contracts/IBpmnBehaviorContext.cs",
        "Contracts/IBpmnBehaviorRegistry.cs",
        "Contracts/IBpmnElementBehavior.cs",
        "Exceptions/BpmnExecutionException.cs",
        "Internal/Behaviors/BpmnBoundaryBehaviors.cs",
        "Internal/Behaviors/BpmnGatewayBehaviors.cs",
        "Internal/Behaviors/BpmnFlowSelector.cs",
        "Internal/Behaviors/BpmnEventBehaviors.cs",
        "Internal/Behaviors/BpmnTaskBehaviors.cs",
        "Internal/BpmnBehaviorContext.cs",
        "Internal/BpmnBehaviorRegistry.cs",
        "Internal/BpmnDiagnosticAccumulator.cs",
        "Internal/BpmnElementFamilies.cs",
        "Internal/BpmnExecutionEngine.cs",
        "Internal/BpmnGraph.cs",
        "Internal/BpmnScheduler.cs",
        "Internal/BpmnStateMutator.cs",
        "Internal/BpmnStatePersister.cs",
        "Internal/BpmnTokenCoordinator.cs",
        "Models/BpmnActiveChild.cs",
        "Models/BpmnBehaviorDecision.cs",
        "Models/BpmnCompensable.cs",
        "Models/BpmnCompensationRun.cs",
        "Models/BpmnDiagnosticEvent.cs",
        "Models/BpmnEventRace.cs",
        "Models/BpmnExecutionState.cs",
        "Models/BpmnLoopState.cs",
        "Models/BpmnToken.cs",
    ];
}
