namespace Elsa.Expressions.Core.Contracts
{
    public interface IVariable<TValue> : IVariable
    {        
        public IVariable<TValue> WithId(string id)
        {
            Id = id;
            return this;
        }

        public IVariable<TValue> WithName(string name)
        {
            Name = name;
            return this;
        }

        public IVariable<TValue> WithValue(TValue value)
        {
            Value = value;
            return this;
        }
    }
}
