using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Projects authored variable declarations from an executable container into durable frame values.
/// This is a declaration compiler only; it creates no mutable expression variables or memory blocks.
/// </summary>
public sealed class RuntimeVariableDeclarationProjector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyDictionary<string, RuntimeVariableDeclaration> ProjectDeclarations(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Structure is null)
            return EmptyDeclarations;

        var projection = node.Structure.Payload.Deserialize<StructureVariablesProjection>(SerializerOptions)
            ?? throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' has malformed variable declaration structure.");

        return ProjectDeclarations(projection?.Variables ?? []);
    }

    public IReadOnlyDictionary<string, RuntimeVariableDeclaration> ProjectDeclarations(
        IReadOnlyCollection<RuntimeVariableDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.Count == 0)
            return EmptyDeclarations;

        return declarations
            .Where(declaration => !string.IsNullOrWhiteSpace(declaration.VariableKey))
            .ToDictionary(declaration => declaration.VariableKey, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, ValueEnvelope> ProjectInitialValues(ExecutableNode node) =>
        ProjectInitialValues(ProjectDeclarations(node));

    public IReadOnlyDictionary<string, ValueEnvelope> ProjectInitialValues(
        IReadOnlyCollection<RuntimeVariableDeclaration> declarations) =>
        ProjectInitialValues(ProjectDeclarations(declarations));

    private static IReadOnlyDictionary<string, ValueEnvelope> ProjectInitialValues(
        IReadOnlyDictionary<string, RuntimeVariableDeclaration> declarations)
    {
        if (declarations.Count == 0)
            return EmptyValues;

        return declarations.ToDictionary(
            item => item.Key,
            item => ToEnvelope(item.Value),
            StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, object?> ProjectDeclaredVariableDefaultsByName(
        IReadOnlyCollection<RuntimeVariableDeclaration> declarations) =>
        ProjectDeclarations(declarations).Values
            .Select(declaration => (Declaration: declaration, Envelope: ToEnvelope(declaration)))
            .Where(item => item.Envelope.Presence != ValuePresence.Absent)
            .ToDictionary(
                item => item.Declaration.Name,
                item => item.Envelope.Presence == ValuePresence.ExplicitNull
                    ? null
                    : (object?)item.Envelope.InlineValue!.Value.Clone(),
                StringComparer.Ordinal);

    private static ValueEnvelope ToEnvelope(RuntimeVariableDeclaration declaration)
    {
        if (declaration.InitialBinding is null)
            return ValueEnvelope.Absent(declaration.Type, declaration.Policy);
        if (declaration.InitialBinding is not { Source: RuntimeInputBindingSource.Literal, Literal: { } literal })
            throw new InvalidOperationException($"Variable '{declaration.VariableKey}' uses a non-literal initial binding that must be materialized before frame activation.");
        return new ValueEnvelope(
            declaration.Type,
            literal.Presence,
            literal.InlineValue,
            literal.ExternalReference,
            declaration.Policy);
    }

    private static readonly IReadOnlyDictionary<string, RuntimeVariableDeclaration> EmptyDeclarations =
        new Dictionary<string, RuntimeVariableDeclaration>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ValueEnvelope> EmptyValues =
        new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);

    private sealed class StructureVariablesProjection
    {
        [JsonPropertyName("variables")]
        public IReadOnlyCollection<RuntimeVariableDeclaration>? Variables { get; init; }
    }
}
