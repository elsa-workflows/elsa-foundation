using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Serialization.Core;

namespace Elsa.Secrets.Expressions;

public sealed class SecretExpressionHandler(ISecretResolver resolver, IObjectConverter objectConverter) : IExpressionHandler
{
    public async ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options)
    {
        var reference = ToReference(expression.Value);
        var result = await resolver.ResolveAsync(reference);

        if (!result.Succeeded)
            throw new InvalidOperationException(result.Error ?? "Secret could not be resolved.");

        if (returnType == typeof(string) || returnType == typeof(object))
            return result.Value;

        return objectConverter.ConvertTo(result.Value, returnType);
    }

    private static SecretReference ToReference(object? value)
    {
        if (value is SecretReference reference)
            return reference;

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
                return new(jsonElement.GetString() ?? "");

            var deserialized = jsonElement.Deserialize<SecretReference>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (deserialized is not null)
                return deserialized;
        }

        return new($"{value}");
    }
}
