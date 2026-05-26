namespace Elsa.Mapping.Core.Contracts;

public interface IObjectMapping<in TSource, TTarget>
{
    ValueTask<TTarget> Map(TSource source, CancellationToken cancellationToken = default);
}
