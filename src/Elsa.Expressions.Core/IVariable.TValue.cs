namespace Elsa.Expressions.Core
{
    public interface IVariable<TValue> : IVariable
    {        
        /// <summary>
        /// Gets the value of the variable.
        /// </summary>
        TValue? Get(IExpressionExecutionContext context);      

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
