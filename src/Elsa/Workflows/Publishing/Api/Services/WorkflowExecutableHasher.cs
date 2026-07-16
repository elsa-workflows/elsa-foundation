using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            RuntimeInputBindingSource.Literal when input.Value.Literal is not null => FormatEnvelope(input.Value.Literal),
            RuntimeInputBindingSource.WorkflowRequest =>
                $"request:{input.Value.WorkflowRequest?.MemberKey}:{input.Value.WorkflowRequest?.Path}",
            RuntimeInputBindingSource.VariableRead =>
                $"variable:{input.Value.Variable?.DeclaringScopeId}:{input.Value.Variable?.VariableKey}",
            RuntimeInputBindingSource.ActivityResult =>
                $"result:{input.Value.ActivityResult?.ProducerScopeId}:{input.Value.ActivityResult?.ProducerExecutableNodeId}:{input.Value.ActivityResult?.ProjectionKey}:{input.Value.ActivityResult?.IsOptional}",
            RuntimeInputBindingSource.Expression => FormatExpression(input.Value.Expression),
            _ => CanonicalJson(input.Value.LiteralValue)
        };

        return $"{input.Key}:{FormatType(input.Value.TargetType)}:{FormatPolicy(input.Value.EffectivePolicy)}={payload}[{metadata}]";
    }

    private static string FormatEnvelope(ValueEnvelope envelope)
    {
        var externalMetadata = envelope.ExternalReference is null
            ? string.Empty
            : string.Join(',', envelope.ExternalReference.Metadata
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{item.Key}={item.Value}"));
        var payload = envelope.InlineValue.HasValue
            ? CanonicalJson(envelope.InlineValue)
            : envelope.ExternalReference is null
                ? string.Empty
                : $"{envelope.ExternalReference.StorageProfile}:{envelope.ExternalReference.Locator}[{externalMetadata}]";

        return $"{envelope.Presence}:{FormatType(envelope.Type)}:{FormatPolicy(envelope.Policy)}:{payload}";
    }

    private static string FormatExpression(RuntimeExpressionBinding? expression)
    {
        if (expression is null)
            return string.Empty;

        var metadata = string.Join(',', expression.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        var resultType = expression.ResultType is null
            ? string.Empty
            : $"{expression.ResultType.Kind}:{expression.ResultType.Id}:{CanonicalJson(expression.ResultType.Schema)}";
        var parameters = string.Join(',', expression.Parameters
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={CanonicalJson(JsonSerializer.SerializeToElement(item.Value, typeof(Elsa.Expressions.Core.Models.ExpressionParameterBinding)))}"));
        return $"expression:{expression.Language}:{expression.Expression}:{resultType}:{expression.CapabilityProfile}:{CanonicalJson(expression.Options)}:({parameters})[{metadata}]";
    }

    private static string FormatType(Elsa.Primitives.Models.ValueTypeDescriptor type) =>
        $"{type.Alias}:{type.CollectionKind}:{type.SchemaVersion}:{CanonicalJson(type.Schema)}";

    private static string FormatPolicy(ValueProtectionPolicy policy)
    {
        var metadata = string.Join(',', policy.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));
        return $"{policy.Lifecycle}:{policy.Storage}:{policy.IsSensitive}:{policy.RequiresEncryption}:{policy.RedactionMode}:{policy.RetentionPolicy}[{metadata}]";
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
            : $"{node.Structure.Kind}:{node.Structure.SchemaVersion}:{CanonicalJson(node.Structure.Payload)}";
        var legacyShape = $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.DescriptorType}:{CanonicalJson(node.DescriptorPayload)}:{structure}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(FormatInputBinding))}:{childSlots}";
        return node.ActivityContract is null
            ? legacyShape
            : $"{legacyShape}:contract:{node.ActivityContract.SchemaFingerprint}";
    }

    private static string CanonicalJson(JsonElement? element)
    {
        if (!element.HasValue)
            return string.Empty;

        var node = JsonNode.Parse(element.Value.GetRawText());
        return node is null ? "null" : SortNode(node).ToJsonString();
    }

    private static JsonNode SortNode(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => KeyValuePair.Create(item.Key, item.Value is null ? null : SortNode(item.Value)))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : SortNode(item)).ToArray()),
        _ => node.DeepClone()
    };

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
