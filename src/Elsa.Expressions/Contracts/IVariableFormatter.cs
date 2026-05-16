using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Contracts
{
    public interface IVariableFormatter
    {
        string? Format(IVariable value);
    }
}
