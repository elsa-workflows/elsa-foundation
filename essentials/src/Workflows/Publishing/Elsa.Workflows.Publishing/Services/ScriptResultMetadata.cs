using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

internal static class ScriptResultMetadata
{
    internal static IReadOnlyDictionary<string, string> Pin(string? inputKey,
        IReadOnlyDictionary<string, RuntimeInputBinding> bindings, IReadOnlyDictionary<string, string> metadata,
        IJavaScriptScriptOutcomeAnalyzer? analyzer, bool consumed, string activityId)
    {
        var result = metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        result.Remove(RuntimeMetadataKeys.ScriptBodyWithoutReturn);
        if (inputKey is null) return result;
        result[RuntimeMetadataKeys.ScriptResultInputKey] = inputKey;
        if (bindings.TryGetValue(inputKey, out var body) && body.LiteralValue is { ValueKind: JsonValueKind.String } text)
        {
            if (consumed && analyzer is null)
                throw new InvalidOperationException("A consumed JavaScript result needs the registered script outcome analyzer.");
            if (analyzer?.Analyze(text.GetString()!) is { HasReturnStatement: false })
                result[RuntimeMetadataKeys.ScriptBodyWithoutReturn] = "true";
        }
        if (consumed && result.ContainsKey(RuntimeMetadataKeys.ScriptBodyWithoutReturn))
            throw new ArgumentException($"VF-UNDEF-001: Activity '{activityId}' has a bound script result but its body has no return statement; return a value or remove the result binding.");
        return result;
    }
}
