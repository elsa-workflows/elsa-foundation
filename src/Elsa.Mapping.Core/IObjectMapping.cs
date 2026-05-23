namespace Elsa.Mapping.Contracts;

public interface IObjectMapping<TSource, TTarget>
{
    TTarget Map(TSource source);
}
