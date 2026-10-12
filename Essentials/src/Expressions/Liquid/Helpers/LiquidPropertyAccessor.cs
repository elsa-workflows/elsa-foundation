using Fluid.Values;

namespace Elsa.Expressions.Liquid.Helpers;

/// <summary>
/// Can be used to provide a factory to return a value based on a property name 
/// that is unknown at registration time. 
/// 
/// e.g. {{ LiquidPropertyAccessor.MyPropertyName }} (MyPropertyName will be passed as the identifier argument to the factory)
/// </summary>
public sealed class LiquidPropertyAccessor(Func<string, Task<FluidValue>> getter) : LiquidObjectAccessor<FluidValue>(getter!)
{
}