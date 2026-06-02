using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JsonConverters;
using Elsa.Serialization.Core;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.Sources;

/// <summary>
/// Contributes the Expressions feature's JSON converters: a <see cref="VariableConverterFactory"/>
/// for <c>Variable&lt;T&gt;</c> payloads (resolves via <see cref="IVariableMapper"/>), and a
/// <see cref="FuncExpressionValueConverter"/> for func-expression value payloads.
/// </summary>
public sealed class ExpressionsJsonConverterSource(IVariableMapper variableMapper) : IJsonConverterSource
{
    public IEnumerable<JsonConverter> GetConverters()
    {
        yield return new VariableConverterFactory(variableMapper);
        yield return new FuncExpressionValueConverter();
    }
}
