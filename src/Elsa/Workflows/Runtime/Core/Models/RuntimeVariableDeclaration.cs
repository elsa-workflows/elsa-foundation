using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Canonical executable declaration for one persistable lexical variable.</summary>
public sealed class RuntimeVariableDeclaration
{
    public RuntimeVariableDeclaration(
        string variableKey,
        string name,
        ValueTypeDescriptor type,
        ValueProtectionPolicy policy,
        RuntimeInputBinding? initialBinding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Lifecycle == DurableValueLifecycle.None)
            throw new ArgumentException("A lexical variable requires a persistable protection policy.", nameof(policy));
        if (initialBinding is not null &&
            (!StringComparer.Ordinal.Equals(initialBinding.TargetType.Alias, type.Alias) ||
             initialBinding.TargetType.CollectionKind != type.CollectionKind ||
             !initialBinding.EffectivePolicy.Satisfies(policy)))
        {
            throw new ArgumentException("A variable initial binding must preserve the declaration type and protection policy.", nameof(initialBinding));
        }

        VariableKey = variableKey;
        Name = name;
        Type = type;
        Policy = policy;
        InitialBinding = initialBinding;
    }

    public string VariableKey { get; }
    public string Name { get; }
    public ValueTypeDescriptor Type { get; }
    public ValueProtectionPolicy Policy { get; }
    public RuntimeInputBinding? InitialBinding { get; }
}
