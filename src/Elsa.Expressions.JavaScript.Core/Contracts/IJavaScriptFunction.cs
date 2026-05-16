namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptFunction
    {
        string Name { get; }

        object? Execute(IJavaScriptExecutionContext context, object[] parameters);
    }
}
