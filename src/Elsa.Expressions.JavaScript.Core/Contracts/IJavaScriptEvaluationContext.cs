namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptEvaluationContext
    {
        void SetValue(string name, object? value);

        void AddFunction(IJavaScriptFunction function);

        void AddType(Type type);

        object? GetValue(string name);

        TValue? GetValue<TValue>(string name);

        void AddResource(Stream resource);
    }
}
