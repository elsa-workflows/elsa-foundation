using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Contracts
{
    public interface IVariableDefaultValueFormatter
    {
        ArgumentValue? Format(object? value);
    }
}
