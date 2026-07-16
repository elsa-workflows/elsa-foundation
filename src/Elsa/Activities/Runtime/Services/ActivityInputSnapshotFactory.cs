using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

internal static class ActivityInputSnapshotFactory
{
    public static ActivityInputSnapshot Create(
        ExecutableNode node,
        string invocationId,
        ActivityContract contract,
        IReadOnlyCollection<RuntimeMaterializedActivityInput> inputs,
        DateTimeOffset materializedAt)
    {
        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!node.InputBindings.TryGetValue(input.Name, out var binding))
                throw new InvalidOperationException($"Materialized input '{input.Name}' has no pinned binding on executable node '{node.ExecutableNodeId}'.");

            values.Add(
                input.Name,
                input.Value is null
                    ? ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy)
                    : ValueEnvelope.Inline(
                        binding.TargetType,
                        JsonSerializer.SerializeToElement(input.Value, input.Value.GetType()),
                        binding.EffectivePolicy));
        }

        var bindingFingerprint = ComputeBindingFingerprint(node);
        return new ActivityInputSnapshot(
            invocationId,
            contract.SchemaFingerprint,
            bindingFingerprint,
            values,
            materializedAt);
    }

    private static string ComputeBindingFingerprint(ExecutableNode node)
    {
        var canonical = string.Join('|', node.InputBindings
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}:{item.Value.Source}:{item.Value.TargetType.Alias}:{item.Value.TargetType.CollectionKind}:{item.Value.Literal?.InlineValue?.GetRawText()}:{item.Value.Expression?.Language}:{item.Value.Expression?.Expression}"));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
