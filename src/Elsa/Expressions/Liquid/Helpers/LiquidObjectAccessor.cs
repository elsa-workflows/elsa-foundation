namespace Elsa.Expressions.Liquid.Helpers;

/// <summary>
/// Can be used to provide a factory to return an object based on a property name that is unknown at registration time. 
/// </summary>
public class LiquidObjectAccessor<T>(Func<string, Task<T>> getter)
{
    public Task<T> GetValueAsync(string identifier) => getter(identifier);
}