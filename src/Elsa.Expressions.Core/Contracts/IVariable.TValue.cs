namespace Elsa.Expressions.Core.Contracts
{
    public interface IVariable<TValue> : IVariable
    {
        IVariable<TValue> WithId(string id)
        {
            Id = id;
            return this;
        }

        IVariable<TValue> WithName(string name)
        {
            Name = name;
            return this;
        }

        IVariable<TValue> WithValue(TValue value)
        {
            Value = value;
            return this;
        }
    }
}
