namespace Elsa.Expressions.Liquid.Options
{
    public sealed record ConfigureLiquidEngineOptions(
        IEnumerable<Type> VariableDescriptorTypes,
        bool AllowConfigurationAccess
    );
}
