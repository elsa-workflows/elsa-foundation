using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>One canonical variable policy, secret/default and literal conversion implementation.</summary>
internal static class RuntimeVariableDeclarationCompiler
{
    internal static RuntimeVariableDeclaration Compile(string nodeId, VariableDefinition variable, RuntimeInputBindingCompiler inputBindingCompiler)
    {
        var type = new ValueTypeDescriptor(variable.Type.Alias, variable.Type.CollectionKind);
        // The built-in JSON driver encodes into the checkpoint itself. It is not an external
        // storage profile; Studio advertises this driver for durable variable declarations.
        var storageProfile = StringComparer.Ordinal.Equals(variable.StorageDriverType, WellKnownRuntimeDurableValueStorageDrivers.Json)
            ? null
            : variable.StorageDriverType;
        var policy = ValuePolicyCombiner.ToProtectionPolicy(
            ValuePolicyCombiner.FromAuthoredStorage(storageProfile));
        if (policy.Storage != DurableValueStorage.Inline)
        {
            throw new ArgumentException(
                $"VF-ACT-005: Variable '{variable.ReferenceKey}' on activity node '{nodeId}' selects external storage profile '{variable.StorageDriverType}', but canonical variable-frame externalization is not implemented. Publication is refused instead of accepting an unusable declaration.");
        }
        RuntimeInputBinding? initialBinding = null;
        if (RuntimeInputBindingCompiler.IsSecretBinding(variable.Default))
            throw SecretBindingDiagnostics.VariableDefaultRefused(nodeId, variable.ReferenceKey);
        if (variable.Default is not null)
        {
            initialBinding = inputBindingCompiler.Compile(
                nodeId,
                new InputDefinition(
                    variable.ReferenceKey,
                    variable.Name,
                    variable.Type,
                    storageProfile,
                    variable.Name,
                    Category: null,
                    IsNullable: true),
                variable.Default);
            if (initialBinding.Source != RuntimeInputBindingSource.Literal)
                throw new ArgumentException($"Variable '{variable.ReferenceKey}' on activity node '{nodeId}' requires a persistable literal initial binding.");
        }

        return new RuntimeVariableDeclaration(variable.ReferenceKey, variable.Name, type, policy, initialBinding);
    }
}
