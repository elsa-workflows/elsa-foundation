namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptExecutionContext
    {
        object? GetValue(string name);

        public TValue? GetValue<TValue>(string name) => (TValue?)GetValue(name);

        void SetValue(string name, object? value);

        void RegisterFunction(IJavaScriptFunction function);

        void RegisterObject(IJavaScriptObject @object);

        void RegisterType(Type type);

        object? NormalizeValue(object? value);

        object? Evaluate(string script);

        void Execute(string script);
    }
}
