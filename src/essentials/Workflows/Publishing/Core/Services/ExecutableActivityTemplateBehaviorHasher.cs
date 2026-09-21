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
        var behavior = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = "1",
            root,
            resumeTargets = resumeTargets.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            directDependencies = directDependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal).ThenBy(x => x.DefinitionVersionId, StringComparer.Ordinal).ToArray(),
            closedTemplates = closedTemplates.OrderBy(x => x.TemplateHash, StringComparer.Ordinal).ThenBy(x => x.TemplateId, StringComparer.Ordinal).ToArray(),
            runtimeRequirements = runtimeRequirements.OrderBy(x => x.ConsumerKey, StringComparer.Ordinal).ThenBy(x => x.SchemaVersion, StringComparer.Ordinal).ToArray(),
            storageDriverRequirements = storageDriverRequirements.OrderBy(x => x.DriverKey, StringComparer.Ordinal).ToArray(),
            providerFingerprint,
            compatibilityMetadata = compatibilityMetadata.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray()
        });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, behavior);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()))}";
    }

    public static string ComputeCanonicalValueHash(object value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()))}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
