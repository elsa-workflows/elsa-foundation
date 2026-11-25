using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Services;

/// <summary>Writes a declared default through its typed conversion contract.</summary>
internal sealed class VariableDefaultValueFormatter : IVariableDefaultValueFormatter
{
    public ArgumentValue? Format(object? value, Type? declaredType = null) =>
        value is null ? null : new(JsonConversionContract.SerializeToElement(value, declaredType));
}
