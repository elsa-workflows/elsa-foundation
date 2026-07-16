using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Computes the content-addressable identity of a compiled workflow executable: the deterministic SHA-256
/// <c>ArtifactHash</c> over a canonical rendering of the executable node tree, and the derived
/// <c>ArtifactId</c>. Extracted from <see cref="WorkflowExecutableCompiler"/> (W30b, #418) so hashing and
/// artifact-id formatting can change independently of activity-tree compilation.
/// </summary>
/// <remarks>
/// The canonical payload shape is wire-significant: any change here changes every artifact hash and id. The
/// characterization goldens pin both. Per ADR 0038 the payload is <b>behavioral-only</b>: it covers the
/// canonical node tree (root node id plus the flattened, ordinally-ordered node renderings) and carries no
/// source identity, so equal hash ⇔ equal behavior in both directions and executables are content-addressed.
/// </remarks>
public sealed class WorkflowExecutableHasher
{
    private const string ArtifactHashPrefix = "sha256:";
    private const int ArtifactIdHashLength = 12;

    public string ComputeHash(ExecutableNode rootActivity)
    {
        return Hash(NodePayload(rootActivity));
    }

    public string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);
        ArgumentNullException.ThrowIfNull(inputContract);
        ArgumentNullException.ThrowIfNull(dependencies);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteBehavioralPayload(writer, rootActivity, inputContract, dependencies);

        return Hash(stream.ToArray());
    }

    private static string NodePayload(ExecutableNode rootActivity)
    {
        ArgumentNullException.ThrowIfNull(rootActivity);
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        return string.Join(
            '\n',
            rootActivity.ExecutableNodeId,
            string.Join('|', nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
                .Select(FormatNode)));
    }

    private static string Hash(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string Hash(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public string CreateArtifactId(string artifactIdPrefix, string artifactHash)
    {
        if (!artifactHash.StartsWith(ArtifactHashPrefix, StringComparison.Ordinal) ||
            artifactHash.Length < ArtifactHashPrefix.Length + ArtifactIdHashLength)
            throw new ArgumentException($"Artifact hash '{artifactHash}' does not use the expected '{ArtifactHashPrefix}' format.", nameof(artifactHash));

        return $"{artifactIdPrefix}{artifactHash[ArtifactHashPrefix.Length..(ArtifactHashPrefix.Length + ArtifactIdHashLength)]}";
    }

    private static string FormatInputBinding(KeyValuePair<string, RuntimeInputBinding> input)
    {
        var metadata = string.Join(',', input.Value.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));

        var payload = input.Value.Source switch
        {
            RuntimeInputBindingSource.Expression => $"{input.Value.Source}:{input.Value.Expression?.Language}:{input.Value.Expression?.Expression}",
            _ => input.Value.LiteralValue?.GetRawText()
        };

        var sensitivity = input.Value.IsSensitive ? "[sensitive=true]" : string.Empty;
        return $"{input.Key}={payload}[{metadata}]{sensitivity}";
    }

    private static string FormatNode(ExecutableNode node)
    {
        var childSlots = string.Join(',', node.ChildSlots
            .OrderBy(slot => slot.Name, StringComparer.Ordinal)
            .Select(slot =>
            {
                var activities = string.Join(';', slot.Activities.Select(activity => activity.ExecutableNodeId).Order(StringComparer.Ordinal));
                return $"{slot.Name}({activities})";
            }));
        var structure = node.Structure is null
            ? string.Empty
            : $"{node.Structure.Kind}:{node.Structure.SchemaVersion}:{node.Structure.Payload.GetRawText()}";
        return $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.DescriptorType}:{node.DescriptorPayload.GetRawText()}:{structure}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(FormatInputBinding))}:{childSlots}";
    }

    private static void WriteBehavioralPayload(
        Utf8JsonWriter writer,
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("rootNodeId", rootActivity.ExecutableNodeId);

        writer.WriteStartArray("nodes");
        foreach (var node in FlattenExecutableActivities(rootActivity).OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal))
            WriteNode(writer, node);
        writer.WriteEndArray();

        writer.WriteStartObject("inputContract");
        writer.WriteNumber("version", inputContract.Version);
        writer.WriteStartArray("inputs");
        foreach (var input in inputContract.Inputs.OrderBy(input => input.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", input.Name);
            writer.WriteString("typeAlias", input.Type.Alias);
            writer.WriteString("collectionKind", input.Type.CollectionKind.ToString());
            writer.WriteBoolean("isRequired", input.IsRequired);
            writer.WriteBoolean("hasDefaultValue", input.DefaultValue.HasValue);
            writer.WritePropertyName("defaultValue");
            if (input.DefaultValue is { } defaultValue)
                WriteCanonicalJson(writer, defaultValue);
            else
                writer.WriteNullValue();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("dependencies");
        foreach (var dependency in dependencies
                     .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
                     .ThenBy(dependency => dependency.ArtifactHash, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("artifactId", dependency.ArtifactId);
            writer.WriteString("artifactHash", dependency.ArtifactHash);
            writer.WriteStartArray("dispatchNodeIds");
            foreach (var nodeId in dependency.DispatchNodeIds.Order(StringComparer.Ordinal))
                writer.WriteStringValue(nodeId);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter writer, ExecutableNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("nodeId", node.ExecutableNodeId);
        writer.WriteString("activityType", node.ActivityType);
        writer.WriteString("activityTypeVersion", node.ActivityTypeVersion);
        writer.WriteString("descriptorType", node.DescriptorType);
        writer.WritePropertyName("descriptorPayload");
        WriteCanonicalJson(writer, node.DescriptorPayload);

        writer.WritePropertyName("structure");
        if (node.Structure is { } structure)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", structure.Kind);
            writer.WriteString("schemaVersion", structure.SchemaVersion);
            writer.WritePropertyName("payload");
            WriteCanonicalJson(writer, structure.Payload);
            writer.WriteEndObject();
        }
        else
            writer.WriteNullValue();

        writer.WriteStartArray("inputBindings");
        foreach (var (inputName, binding) in node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", inputName);
            writer.WriteString("source", binding.Source.ToString());
            writer.WritePropertyName("payload");
            if (binding.Source == RuntimeInputBindingSource.Expression)
            {
                writer.WriteStartObject();
                writer.WriteString("language", binding.Expression?.Language);
                writer.WriteString("expression", binding.Expression?.Expression);
                writer.WriteEndObject();
            }
            else if (binding.LiteralValue is { } literalValue)
                WriteCanonicalJson(writer, literalValue);
            else
                writer.WriteNullValue();

            writer.WriteStartObject("metadata");
            foreach (var (key, value) in binding.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
                writer.WriteString(key, value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("childSlots");
        foreach (var slot in node.ChildSlots.OrderBy(slot => slot.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", slot.Name);
            writer.WriteStartArray("nodeIds");
            foreach (var activity in slot.Activities.OrderBy(activity => activity.ExecutableNodeId, StringComparer.Ordinal))
                writer.WriteStringValue(activity.ExecutableNodeId);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static IEnumerable<ExecutableNode> FlattenExecutableActivities(ExecutableNode rootActivity)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
