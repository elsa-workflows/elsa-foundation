using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Computes the content-addressable identity of a compiled workflow executable: the deterministic SHA-256
/// <c>ArtifactHash</c> over the source identity plus a canonical rendering of the executable node tree, and the
/// derived <c>ArtifactId</c>. Extracted from <see cref="WorkflowExecutableCompiler"/> (W30b, #418) so hashing
/// and artifact-id formatting can change independently of activity-tree compilation.
/// </summary>
/// <remarks>
/// The canonical payload shape is wire-significant: any change here changes every artifact hash and id. The
/// characterization goldens pin both across the W30b decomposition.
/// </remarks>
public sealed class WorkflowExecutableHasher
{
    private const string ArtifactHashPrefix = "sha256:";
    private const int ArtifactIdHashLength = 12;

    public string ComputeHash(WorkflowExecutableCompileSource source, ExecutableNode rootActivity)
    {
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        var payload = string.Join(
            '\n',
            source.SourceReference.SourceKind,
            source.SourceReference.SourceId,
            source.SourceReference.SourceVersion,
            source.DefinitionId,
            source.DefinitionVersionId,
            source.ArtifactVersion,
            rootActivity.ExecutableNodeId,
            string.Join('|', nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
                .Select(FormatNode)));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
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

        return $"{input.Key}={payload}[{metadata}]";
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
