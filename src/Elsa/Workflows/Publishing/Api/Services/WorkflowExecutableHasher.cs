using System.Security.Cryptography;
using System.Text;
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
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        var payload = string.Join(
            '\n',
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

    private static string FormatOutputCapture(KeyValuePair<string, RuntimeOutputCapture> output)
    {
        var capture = output.Value;
        var metadata = string.Join(',', capture.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var schema = capture.Type.Schema?.GetRawText() ?? string.Empty;
        return $"{output.Key}={capture.OutputName}:{capture.ValueId}:{capture.Type.Kind}:{capture.Type.Id}:{schema}:" +
               $"{capture.Lifecycle}:{capture.Storage}:{capture.StorageDriverKey}:{capture.CaptureOnSuccessfulCompletion}[{metadata}]";
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
        var outputCaptures = string.Join(',', node.OutputCaptures
            .OrderBy(output => output.Key, StringComparer.Ordinal)
            .Select(FormatOutputCapture));
        var outputCapturePayload = outputCaptures.Length == 0 ? string.Empty : $":outputs={outputCaptures}";
        return $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.Descriptor.ConsumerKey}:{node.Descriptor.SchemaVersion}:{node.Descriptor.Payload.GetRawText()}:{structure}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(FormatInputBinding))}:{childSlots}{outputCapturePayload}";
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
