using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime.Services;

/// <summary>Qualifies the graph owner's closed JSON value scope without creating an enclosing invocation.</summary>
internal sealed class GraphTestScopeContractSource : IRuntimeTestScopeContractSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<CollectionKind>() }
    };

    public ValueTask<RuntimeTestScopeContract?> GetContractAsync(WorkflowExecutable executable,
        ExecutableNode scopeNode, VariableFrameKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Resolve the canonical pinned node, never declarations supplied in a caller-created node copy.
        if (!executable.NodesById.TryGetValue(scopeNode.ExecutableNodeId, out var node) || kind != VariableFrameKind.Root ||
            node.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.GraphActivity ||
            node.Descriptor.SchemaVersion != RuntimeActivityDescriptor.InitialSchemaVersion ||
            node.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityExecutionScope) != RuntimeMetadataKeys.ActivityExecutionScopeClosed ||
            node.Descriptor.Payload.ValueKind != JsonValueKind.Object ||
            !node.Descriptor.Payload.TryGetProperty("scopeModel", out var scopeModel) ||
            scopeModel.ValueKind != JsonValueKind.String || scopeModel.GetString() != RuntimeMetadataKeys.ActivityExecutionScopeClosed)
            return ValueTask.FromResult<RuntimeTestScopeContract?>(null);

        if (node.ActivityContract is not { } contract || contract.DescriptorKind != WellKnownRuntimeActivityConsumers.GraphActivity)
            throw new RuntimeTestContextUnavailableException("graph.test-scope.contract-missing", "Graph test scope is missing its exact pinned activity contract.", node.ExecutableNodeId);
        GraphActivityDescriptor descriptor;
        try
        {
            descriptor = node.Descriptor.Payload.Deserialize<GraphActivityDescriptor>(Json)
                ?? throw new RuntimeTestContextUnavailableException("graph.test-scope.descriptor-missing", "Graph test scope descriptor is unavailable.", node.ExecutableNodeId);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw new RuntimeTestContextUnavailableException("graph.test-scope.descriptor-invalid", "Graph test scope descriptor is invalid for its supported schema.", node.ExecutableNodeId, innerException: exception);
        }
        if (node.Metadata.GetValueOrDefault("activity.definitionId") != descriptor.DefinitionId ||
            node.Metadata.GetValueOrDefault("activity.definitionVersionId") != descriptor.DefinitionVersionId ||
            node.Metadata.GetValueOrDefault("activity.version") != descriptor.Version ||
            node.Metadata.GetValueOrDefault("activity.templateHash") != descriptor.TemplateHash ||
            node.Metadata.GetValueOrDefault("graph.templateHash") != descriptor.TemplateHash)
            throw new RuntimeTestContextUnavailableException("graph.test-scope.pin-mismatch", "Graph test scope dependency identity differs from its exact placement.", node.ExecutableNodeId);

        var variables = new Dictionary<string, RuntimeVariableDeclaration>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in descriptor.Variables)
        {
            if (variable.StorageDriverKey != WellKnownRuntimeDurableValueStorageDrivers.Json)
                throw new RuntimeTestContextUnavailableException("graph.test-scope.storage-unavailable", "Graph test variable context requires a qualified JSON storage profile; the selected storage driver is unavailable.", node.ExecutableNodeId, variable.ReferenceKey);
            if (string.IsNullOrWhiteSpace(variable.ReferenceKey) || string.IsNullOrWhiteSpace(variable.Name) || !names.Add(variable.Name))
                throw new RuntimeTestContextUnavailableException("graph.test-scope.variables-invalid", "Graph test variable declarations require unique stable keys and names.", node.ExecutableNodeId);
            var type = ReadType(variable.Type, node.ExecutableNodeId, variable.ReferenceKey);
            var policy = ValueProtectionPolicy.InstanceInline;
            RuntimeInputBinding? initial = null;
            if (variable.InitialValue is { } value && StringComparer.OrdinalIgnoreCase.Equals(value.Syntax, "Literal"))
            {
                var envelope = value.Value.ValueKind == JsonValueKind.Null ? ValueEnvelope.Null(type, policy) : ValueEnvelope.Inline(type, value.Value, policy);
                initial = new(variable.ReferenceKey, type, policy, RuntimeInputBindingSource.Literal, literal: envelope);
            }
            if (!variables.TryAdd(variable.ReferenceKey, new(variable.ReferenceKey, variable.Name, type, policy, initial)))
                throw new RuntimeTestContextUnavailableException("graph.test-scope.variables-ambiguous", "Graph test variable declaration keys are ambiguous.", node.ExecutableNodeId);
        }

        if (descriptor.Inputs.Count != contract.Inputs.Count ||
            descriptor.Inputs.Select(x => x.ReferenceKey).Distinct(StringComparer.Ordinal).Count() != descriptor.Inputs.Count ||
            !descriptor.RequiredInputReferenceKeys.ToHashSet(StringComparer.Ordinal).SetEquals(contract.Inputs.Values.Where(x => x.IsRequired).Select(x => x.Key)))
            throw new RuntimeTestContextUnavailableException("graph.test-scope.inputs-mismatch", "Graph test input declarations differ from their pinned contract.", node.ExecutableNodeId);
        var inputs = new Dictionary<string, ActivityInputContract>(StringComparer.Ordinal);
        foreach (var input in descriptor.Inputs)
        {
            if (input.StorageDriverKey != WellKnownRuntimeDurableValueStorageDrivers.Json ||
                !contract.Inputs.TryGetValue(input.ReferenceKey, out var declared) || ReadType(input.Type, node.ExecutableNodeId, input.ReferenceKey) != declared.Type ||
                (input.Name ?? input.ReferenceKey) != declared.Name)
                throw new RuntimeTestContextUnavailableException("graph.test-scope.input-unavailable", "Graph test input context is not qualified for its pinned type/storage contract.", node.ExecutableNodeId, input.ReferenceKey);
            var literalDefault = input.Default is { } value && StringComparer.OrdinalIgnoreCase.Equals(value.Syntax, "Literal");
            if (input.Default is null && declared.HasDefault || literalDefault &&
                (!declared.HasDefault || declared.DefaultValue is not { } pinned || !JsonElement.DeepEquals(pinned, input.Default!.Value)))
                throw new RuntimeTestContextUnavailableException("graph.test-scope.default-mismatch", "Graph test input default differs from its pinned declaration.", node.ExecutableNodeId, input.ReferenceKey);
            // The runtime contract stores an expression's raw payload as its default. Treating JavaScript text
            // as a string literal would silently change Graph semantics. Such setup requires an explicit value.
            var needsMaterializedDefault = input.Default is not null && !literalDefault;
            inputs.Add(input.ReferenceKey, new(declared.Key, declared.Name, declared.Type,
                declared.IsRequired || needsMaterializedDefault, declared.IsNullable, literalDefault,
                literalDefault ? input.Default!.Value : null, declared.Policy, declared.EditorMetadata,
                declared.IsCredential, declared.ConnectionType) { IsStructureInput = declared.IsStructureInput });
        }
        return ValueTask.FromResult<RuntimeTestScopeContract?>(new(node.ExecutableNodeId, VariableFrameKind.Root, true, variables, inputs, true));
    }

    private static ValueTypeDescriptor ReadType(JsonElement value, string scopeId, string memberKey)
    {
        ValueTypeDescriptor type;
        try
        {
            type = value.Deserialize<ValueTypeDescriptor>(Json)
                ?? throw new RuntimeTestContextUnavailableException("graph.test-scope.type-missing", "Graph test value type is unavailable.", scopeId, memberKey);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            throw new RuntimeTestContextUnavailableException("graph.test-scope.type-invalid", "Graph test value type is invalid for the qualified profile.", scopeId, memberKey, exception);
        }
        if (!Enum.IsDefined(type.CollectionKind) || type.Schema is not null || type.SchemaVersion is not null)
            throw new RuntimeTestContextUnavailableException("graph.test-scope.schema-unavailable", "Graph test value context requires a qualified ordinary type; schema-specific context is unavailable.", scopeId, memberKey);
        return type;
    }
}
