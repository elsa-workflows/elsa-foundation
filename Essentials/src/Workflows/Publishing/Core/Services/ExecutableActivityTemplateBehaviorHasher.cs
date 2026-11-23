using System.Security.Cryptography;
using System.Text.Json;
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
    {
        var behavior = Elsa.Serialization.Core.JsonHashInput.SerializeToElement(new
        {
            schemaVersion = "1",
            root = RuntimeJsonHashProjection.ProjectExecutable(root),
            resumeTargets = resumeTargets.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            directDependencies = directDependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal).ThenBy(x => x.DefinitionVersionId, StringComparer.Ordinal).ToArray(),
            closedTemplates = closedTemplates.OrderBy(x => x.TemplateHash, StringComparer.Ordinal).ThenBy(x => x.TemplateId, StringComparer.Ordinal).ToArray(),
            runtimeRequirements = runtimeRequirements.OrderBy(x => x.ConsumerKey, StringComparer.Ordinal).ThenBy(x => x.SchemaVersion, StringComparer.Ordinal).ToArray(),
            storageDriverRequirements = storageDriverRequirements.OrderBy(x => x.DriverKey, StringComparer.Ordinal).ToArray(),
            providerFingerprint,
            compatibilityMetadata = compatibilityMetadata.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray()
        });
        return "sha256:" + Elsa.Serialization.Core.JsonHashInput.Hash(behavior);
    }

    public static string ComputeCanonicalValueHash(object value) => "sha256:" + Elsa.Serialization.Core.JsonHashInput.Hash(value);
}
