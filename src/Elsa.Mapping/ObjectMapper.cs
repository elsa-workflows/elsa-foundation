using Elsa.Mapping.Core.Contracts;
using Elsa.Primitives.Extensions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Elsa.Mapping;

public sealed class ObjectMapper(IServiceProvider serviceProvider) : IObjectMapper
{
    public ValueTask<TValue> Map<TValue>(object source, CancellationToken cancellationToken)
        where TValue : notnull
    {
        var sourceType = source.GetType();
        var mappingType = typeof(IObjectMapping<,>).MakeGenericType(sourceType, typeof(TValue));
        var mapping = serviceProvider.GetService(mappingType)
            ?? throw new InvalidOperationException($"No service registered for type '{mappingType}'");

        var mapMethod = mappingType.GetMethod(nameof(IObjectMapping<,>.Map), BindingFlags.Public | BindingFlags.Instance, [sourceType])
            ?? throw new InvalidProgramException($"'Map' method could not be retrieved from '{mappingType}'");

        return mapMethod.InvokeAndUnwrapValueTask<TValue>(mapping, [source]);
    }

    public async IAsyncEnumerable<TValue> Map<TValue>(IEnumerable<object> sources, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TValue : notnull
    {
        foreach (var source in sources)
        {
            yield return await Map<TValue>(source, cancellationToken);
        }
    }
}