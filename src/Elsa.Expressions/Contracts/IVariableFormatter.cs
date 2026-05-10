using Elsa.Expressions.Core;

namespace Elsa.Expressions.Contracts
{
    public interface IVariableFormatter
    {
        string? Format(IVariable value);
    }
}
