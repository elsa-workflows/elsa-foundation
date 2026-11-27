using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Testing;

/// <summary>A frozen pre-#3331 artifact, generated on main 98efed4fc with the production v1 hasher.</summary>
internal static class LegacyWorkflowHashFixture
{
    public static WorkflowExecutable Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("legacy-set-v1.json")!;
        return JsonSerializer.Deserialize<WorkflowExecutable>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        })!;
    }

    public static WorkflowExecutable WithBinding(WorkflowExecutable original, RuntimeInputBinding binding, IWorkflowExecutableHasher? reidentifyWith = null)
    {
        var old = original.RootActivity;
        var root = new ExecutableNode(old.ExecutableNodeId, old.AuthoredActivityId, old.ActivityType, old.ActivityTypeVersion,
            old.Descriptor, inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            outputCaptures: old.OutputCaptures, metadata: old.Metadata, childSlots: old.ChildSlots, structure: old.Structure,
            activityContract: old.ActivityContract, intrinsicKind: old.IntrinsicKind,
            intrinsicVariable: old.IntrinsicVariable, executionPlacement: old.ExecutionPlacement);
        return WithRoot(original, root, reidentifyWith);
    }

    public static WorkflowExecutable WithRoot(WorkflowExecutable original, ExecutableNode root,
        IWorkflowExecutableHasher? reidentifyWith = null, WorkflowExecutableIdentity? declaredIdentity = null)
    {
        var identity = declaredIdentity ?? original.Identity;
        if (reidentifyWith is not null)
        {
            var hash = reidentifyWith.ComputeHash(root, original.InputContract!, original.Dependencies,
                original.PersistenceMode, original.WorkflowVariables, original.IncidentStrategy);
            identity = new WorkflowExecutableIdentity(reidentifyWith.CreateArtifactId("artifact-", hash), identity.DefinitionId,
                identity.DefinitionVersionId, identity.ArtifactVersion, hash);
        }
        return new WorkflowExecutable(identity, root, original.ResumeTargets, original.CreatedAt, original.CompatibilityMetadata,
            original.InputContract, original.Dependencies, original.RuntimeRequirements, original.StorageDriverRequirements,
            original.IncidentStrategy, original.PersistenceMode, original.WorkflowVariables);
    }
}
