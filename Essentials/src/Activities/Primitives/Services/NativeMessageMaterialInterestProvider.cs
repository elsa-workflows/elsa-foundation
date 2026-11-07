using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Pure, unregistered whole-native-material strategy for the supported ReceiveMessage receiver domain.</summary>
/// <remarks>Reads immutable hash-covered inputs on every node, including receive-only nodes. No Design, bookmark,
/// expression evaluation, mutable metadata or dependency-worker enrollment enters this strategy. Other native
/// receiver domains must join this aggregate strategy rather than compete as whole-material providers.</remarks>
internal sealed class NativeMessageMaterialInterestProvider : IMessageMaterialInterestProvider
{
    public string ProviderId => "Elsa.NativeMessageMaterial";
    public RuntimeExecutionProtocol? ModelProtocol => null;

    public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.NativeWorkflow is not { } workflow) return MessageMaterialInterestResult.NotRecognized;
        var declarations = new List<MessageMaterialInterestDeclaration>();
        foreach (var node in workflow.Nodes)
        {
            if (node.ActivityType != ReceiveMessage.ActivityType && node.ActivityType != typeof(ReceiveMessage).FullName) continue;
            var start = Read(node, nameof(ReceiveMessage.CanStartWorkflow));
            if (start?.Source != RuntimeInputBindingSource.Literal || start.LiteralValue is not { } mode ||
                mode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Invalid();

            var name = Read(node, nameof(ReceiveMessage.MessageName)) ?? throw Invalid();
            var declared = DeclaredNames(node);
            IReadOnlyCollection<string> names;
            if (name.Source == RuntimeInputBindingSource.Literal)
            {
                if (name.LiteralValue is not { ValueKind: JsonValueKind.String } literal) throw Invalid();
                var fixedName = Text(literal);
                if (declared is { Count: > 0 } && !declared.Contains(fixedName, StringComparer.Ordinal)) throw Invalid();
                // A fixed binding contributes only its actual name; declarations cannot widen fixed-name interest.
                names = [fixedName];
            }
            else names = declared is { Count: > 0 } ? declared : throw Invalid();
            declarations.Add(new(node.ExecutableNodeId, names, mode.GetBoolean()));
        }
        return MessageMaterialInterestResult.Recognized(declarations);
    }

    private static IReadOnlyCollection<string>? DeclaredNames(ExecutableNode node)
    {
        var binding = Read(node, nameof(ReceiveMessage.DeclaredMessageNames));
        if (binding is null) return null;
        if (binding.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { ValueKind: JsonValueKind.Array } array)
            throw Invalid();
        var names = new List<string>();
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || names.Count == 32) throw Invalid();
            names.Add(Text(value));
        }
        // Validate every supplied name even when a fixed binding later narrows the result.
        return names.Count == 0 ? [] : new MessageMaterialInterestDeclaration(node.ExecutableNodeId, names).MessageNames;
    }

    private static RuntimeInputBinding? Read(ExecutableNode node, string key)
    {
        node.InputBindings.TryGetValue(key, out var binding);
        SecretBindingDiagnostics.ThrowIfSecretRead(binding, node.ExecutableNodeId, key);
        return binding;
    }

    private static string Text(JsonElement value)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException) { throw Invalid(); }
    }

    private static ArgumentException Invalid() => new("ReceiveMessage requires canonical names and a literal start mode.");
}
