namespace Elsa.Modularity.Core.Contracts;

/// <summary>Marks the pass-through implementation that an explicitly enrolled host may replace.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DefaultFeatureActivationContextPreparerAttribute : Attribute;
