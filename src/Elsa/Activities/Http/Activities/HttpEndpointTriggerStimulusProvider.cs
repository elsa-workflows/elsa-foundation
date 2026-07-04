using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the <see cref="HttpEndpoint"/> start trigger (W16, on
/// the W7 seam). It recognizes published <see cref="HttpEndpoint"/> nodes and derives their stimulus identity
/// from the authored <see cref="HttpEndpoint.Path"/> literal, so the trigger extractor can index the endpoint at
/// publish time over the pinned artifact.
/// </summary>
/// <remarks>
/// The path must be an authored literal — the stimulus identity is fixed at publish time, before any run
/// exists, so a non-literal (expression-bound) path has no value to hash and throws, failing the publish rather
/// than persisting an unroutable trigger.
/// </remarks>
public sealed class HttpEndpointTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    private const string PathInput = nameof(HttpEndpoint.Path);

    public TriggerStimulusDescriptor? Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, HttpEndpoint.ActivityType))
            return null;

        var path = ReadLiteralString(node, PathInput)
            ?? throw new ArgumentException(
                $"HTTP endpoint trigger node '{node.ExecutableNodeId}' has no literal '{PathInput}'. A start " +
                "trigger's path must be an authored literal so its stimulus is fixed at publish time.");

        return HttpEndpointStimulus.Describe(path);
    }

    private static string? ReadLiteralString(ExecutableNode node, string inputName)
    {
        var binding = node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputName))
            .Value;

        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            return null;

        if (literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var value = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
