using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The one rule for projecting a value out of an externally stored payload, shared by the whole-value external
/// projection in <see cref="RuntimeActivityInputMaterializer"/> and the member read in <see cref="RuntimeMemberRead"/>
/// (#4078), so a member read projects exactly as the whole read of the same external source does. The path is dotted;
/// each segment matches exactly first and then case-insensitively; <c>$</c> is the whole payload. Every failure is coded
/// <c>VF-ACT-004</c>.
/// </summary>
internal static class RuntimeExternalProjection
{
    /// <summary>
    /// The projection path a result reference names, read from the producer's pinned activity contract.
    /// </summary>
    public static string ResultPath(RuntimeActivityResultReference result, string inputName, string nodeId, WorkflowExecutable? executable)
    {
        var producer = executable?.NodesById.GetValueOrDefault(result.ProducerExecutableNodeId)
            ?? throw new InvalidOperationException(
                $"VF-ACT-004: External activity result input '{inputName}' on executable node '{nodeId}' requires the pinned contract of producer node '{result.ProducerExecutableNodeId}'.");
        return producer.ActivityContract?.Result.Projections.GetValueOrDefault(result.ProjectionKey)?.Path
            ?? throw new InvalidOperationException(
                $"VF-ACT-004: Producer node '{result.ProducerExecutableNodeId}' has no result projection '{result.ProjectionKey}' for input '{inputName}' on executable node '{nodeId}'.");
    }

    /// <summary>
    /// Applies <paramref name="path"/> to <paramref name="payload"/>.
    /// </summary>
    public static JsonElement Apply(JsonElement payload, string path, string inputName, string nodeId)
    {
        if (StringComparer.Ordinal.Equals(path, "$"))
            return payload;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (payload.ValueKind != JsonValueKind.Object || !TryGetProperty(payload, segment, out payload))
                throw new InvalidOperationException($"VF-ACT-004: External source path '{path}' for input '{inputName}' on executable node '{nodeId}' is unavailable.");
        }

        return payload;
    }

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property))
            return true;
        foreach (var candidate in value.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, name))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
