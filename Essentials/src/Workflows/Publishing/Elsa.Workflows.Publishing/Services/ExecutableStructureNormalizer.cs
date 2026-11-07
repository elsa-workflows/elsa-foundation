using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>The same semantic/variable normalization used by ordinary workflow node compilation.</summary>
internal sealed class ExecutableStructureNormalizer(RuntimeInputBindingCompiler inputBindingCompiler) : IExecutableStructureNormalizer
{
    public ExecutableActivityStructure? Normalize(string nodeId, ActivityNodeStructure? structure)
    {
        if (structure is null) return null;
        var payload = JsonNode.Parse(structure.Payload.GetRawText()) as JsonObject
            ?? throw new ArgumentException($"Activity node '{nodeId}' has a non-object executable structure payload.");
        if (payload["variables"] is { } variablesNode)
        {
            var authored = variablesNode.Deserialize<IReadOnlyCollection<VariableDefinition>>(DescriptorPayloadSerializer.Options)
                ?? throw new ArgumentException($"Activity node '{nodeId}' has malformed variable declarations.");
            var compiled = authored.Select(variable => RuntimeVariableDeclarationCompiler.Compile(nodeId, variable, inputBindingCompiler)).ToArray();
            payload["variables"] = JsonSerializer.SerializeToNode(compiled, DescriptorPayloadSerializer.Options);
        }
        return new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion,
            JsonSerializer.SerializeToElement(payload, DescriptorPayloadSerializer.Options));
    }
}
