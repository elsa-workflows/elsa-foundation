using Elsa.Mapping.Contracts;
using System.Reflection;

namespace Elsa.Mapping;

public sealed class ObjectMapper(IServiceProvider serviceProvider) : IObjectMapper
{
    public object? Map(object source, Type returnType)
    {
        var sourceType = source.GetType();
        var mappingType = typeof(IObjectMapping<,>).MakeGenericType(sourceType, returnType);
        var mapping = serviceProvider.GetService(mappingType)
            ?? throw new InvalidOperationException($"No service registered for type '{mappingType}'");

        var mapMethod = mappingType.GetMethod(nameof(IObjectMapping<,>.Map), BindingFlags.Public | BindingFlags.Instance, [sourceType])
            ?? throw new InvalidProgramException($"'Map' method could not be retrieved from '{mappingType}'");

        return mapMethod.Invoke(mapping, parameters: [source]);
    }
}