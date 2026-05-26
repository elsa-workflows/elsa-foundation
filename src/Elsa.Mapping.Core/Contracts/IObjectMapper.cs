namespace Elsa.Mapping.Core.Contracts;

public interface IObjectMapper
{
    ValueTask<TValue> Map<TValue>(object source, CancellationToken cancellationToken)
        where TValue : notnull;

    IAsyncEnumerable<TValue> Map<TValue>(IEnumerable<object> sources, CancellationToken cancellationToken)
        where TValue : notnull;
}
