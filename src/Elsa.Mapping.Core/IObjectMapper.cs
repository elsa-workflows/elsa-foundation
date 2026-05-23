namespace Elsa.Mapping.Contracts;

public interface IObjectMapper
{
    object Map(object source, Type returnType);

    public TValue Map<TValue>(object source) => (TValue)Map(source, typeof(TValue));
}
