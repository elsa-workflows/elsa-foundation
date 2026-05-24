namespace Elsa.Mapping.Core.Contracts;

public interface IObjectMapping<in TSource, out TTarget>
{
    TTarget Map(TSource source);
}
