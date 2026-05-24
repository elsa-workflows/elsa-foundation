namespace Elsa.Mapping.Core.Contracts;

public interface IObjectMapper
{
    object Map(object source, Type returnType);    

    public TValue Map<TValue>(object source) => (TValue)Map(source, typeof(TValue));

    public IEnumerable<TValue> Map<TValue>(IEnumerable<object> sources) => sources.Select(Map<TValue>);
}
