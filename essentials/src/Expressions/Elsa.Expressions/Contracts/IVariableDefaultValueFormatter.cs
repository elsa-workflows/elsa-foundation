using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Contracts;

internal interface IVariableDefaultValueFormatter
{
    ArgumentValue? Format(object? value);
}