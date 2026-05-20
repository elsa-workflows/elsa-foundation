namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptFunction
    {
        string Name { get; }

        Delegate Delegate { get; }
    }
}
