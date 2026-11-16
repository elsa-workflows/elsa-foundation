using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Services;

/// <summary>Single canonical behavioral hash for every executable activity template provider.</summary>
public static class ExecutableActivityTemplateBehaviorHasher
{
    public static string Compute(
        ExecutableNode root,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> resumeTargets,
        IReadOnlyCollection<ExecutableActivityTemplateDependency> directDependencies,
        IReadOnlyCollection<ExecutableActivityTemplateIdentity> closedTemplates,
        IReadOnlyCollection<RuntimeRequirement> runtimeRequirements,
        IReadOnlyCollection<RuntimeStorageDriverRequirement> storageDriverRequirements,
        string providerFingerprint,
        IReadOnlyDictionary<string, string> compatibilityMetadata)
        => ActivityTemplateHasher.Compute(root, resumeTargets, directDependencies, closedTemplates,
            runtimeRequirements, storageDriverRequirements, providerFingerprint, compatibilityMetadata);

    public static string ComputeCanonicalValueHash(object value) => ActivityTemplateHasher.ComputeCanonicalValueHash(value);
}
