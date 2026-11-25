using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.Conversion;

internal sealed partial class Elsa3ActivityNormalizer
{
    /// <summary>
    /// A DispatchWorkflow/ExecuteWorkflow input map (#5808). Elsa 3 handed the child any name and matched names
    /// case-insensitively, so a name the child did not declare was ignored. Elsa 4 accepts only the names the child
    /// declares, spelled as declared. A literal map (or a static Object expression map) to a target this import publishes
    /// is checked: a key spelled in another letter case is spelled as declared, as Elsa 3 read it, and an undeclared key
    /// is refused. Any other binding is carried with a warning, because its names are only known at run time.
    /// </summary>
    private static JsonElement? CheckDispatchInputs(Elsa3RewriteContext context, Elsa3MemberRule rule, string propertyName, JsonElement value)
    {
        if (CheckContract(context, rule with { Contract = Elsa3ValueContract.JsonOnly }, propertyName, value) is not { } fitted)
            return null;
        if (!Elsa3TypedValues.IsBound(fitted))
            return fitted;
        // A blank script evaluates to null in Elsa 3, which passes no inputs: unbound, as a blank content type is.
        if (IsBlankExpression(fitted))
            return null;

        var (targetId, declared) = DispatchTarget(context);
        if (!TryGetStaticInputMap(fitted, out var json, out var expressionType))
        {
            context.Warning(DispatchInputNames,
                $"Input '{propertyName}' is not a literal map, so the conversion cannot check its names. Elsa 3 ignored a name the target workflow did not declare and matched names in any letter case; " +
                "Elsa 4 faults the dispatch when a name is not declared by the target, or is spelled in another letter case.",
                declared is null
                    ? "Return only the inputs the target workflow declares, spelled exactly as declared. If the expression already does, nothing needs to change."
                    : $"Return only the inputs '{targetId}' declares ({Describe(declared)}), spelled exactly as declared. If the expression already does, nothing needs to change.",
                propertyName);
            return fitted;
        }

        if (declared is null || !TryReadObject(json, out var properties))
            return fitted;

        var used = new HashSet<string>(StringComparer.Ordinal);
        var rewritten = new List<(string Name, JsonElement Value)>(properties.Count);
        var failed = false;
        var changed = false;
        foreach (var (key, item) in properties)
        {
            var matches = declared.Contains(key, StringComparer.Ordinal)
                ? [key]
                : declared.Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length == 0)
            {
                context.Error(DispatchInputUndeclared,
                    $"Input '{propertyName}' passes '{key}', which workflow '{targetId}' does not declare. Elsa 3 ignored it; Elsa 4 refuses the dispatch.",
                    declared.Count == 0
                        ? $"Remove '{key}' from the map, or declare it as an input of '{targetId}'."
                        : $"Remove '{key}' from the map, or declare it as an input of '{targetId}'. It declares {Describe(declared)}.",
                    propertyName, key);
                failed = true;
                continue;
            }

            if (matches.Length > 1 || !used.Add(matches[0]))
            {
                context.Error(LiteralKeyAmbiguous,
                    matches.Length > 1
                        ? $"Input '{propertyName}' passes '{key}', which matches more than one input of workflow '{targetId}' in another letter case ({Describe(matches)})."
                        : $"Input '{propertyName}' passes input '{matches[0]}' of workflow '{targetId}' more than once.",
                    "Write each input once, spelled exactly as the target workflow declares it.",
                    propertyName, key);
                failed = true;
                continue;
            }

            if (!StringComparer.Ordinal.Equals(matches[0], key))
            {
                context.Info(LiteralKeyRewritten,
                    $"Input '{propertyName}' key '{key}' was spelled as workflow '{targetId}' declares it, '{matches[0]}'; Elsa 3 matched input names in any letter case.",
                    "Spell input names as the target workflow declares them in new workflows.",
                    propertyName, key);
                changed = true;
            }

            rewritten.Add((matches[0], item));
        }

        if (failed)
            return null;
        if (!changed)
            return fitted;

        var text = WriteObject(rewritten);
        if (fitted.ValueKind == JsonValueKind.String)
            return JsonSerializer.SerializeToElement(text, JsonBaseline.Options);
        return string.Equals(expressionType, Elsa3TypedValues.Literal, StringComparison.OrdinalIgnoreCase)
            ? Elsa3TypedValues.WithLiteral(fitted, text)
            : Elsa3TypedValues.WithExpression(fitted, expressionType, JsonSerializer.SerializeToElement(text, JsonBaseline.Options));
    }

    /// <summary>The dispatch's literal target id and, when this import publishes it, the input names it declares.</summary>
    private static (string? TargetId, IReadOnlyList<string>? Declared) DispatchTarget(Elsa3RewriteContext context)
    {
        if (!context.TryGetProperty("WorkflowDefinitionId", out _, out var id) ||
            !Elsa3TypedValues.TryGetLiteral(id, out var targetId) ||
            string.IsNullOrWhiteSpace(targetId))
            return (null, null);
        return (targetId, context.Plan.Request.DispatchTargetInputs.GetValueOrDefault(targetId));
    }

    /// <summary>A map whose names the conversion can read: a literal, or a static Object expression (#5790).</summary>
    private static bool TryGetStaticInputMap(JsonElement value, out string? json, out string expressionType)
    {
        expressionType = Elsa3TypedValues.Literal;
        if (Elsa3TypedValues.TryGetLiteral(value, out json))
            return true;
        if (!TryGetObjectExpression(value, out json))
            return false;
        Elsa3TypedValues.TryGetExpression(value, out expressionType, out _);
        return true;
    }

    /// <summary>The object's properties in order, duplicates kept; false when the text is blank or not a JSON object.</summary>
    private static bool TryReadObject(string? json, out List<(string Name, JsonElement Value)> properties)
    {
        properties = [];
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var property in document.RootElement.EnumerateObject())
                properties.Add((property.Name, property.Value.Clone()));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string WriteObject(IEnumerable<(string Name, JsonElement Value)> properties)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in properties)
            {
                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Describe(IEnumerable<string> names)
    {
        var list = names.Select(name => $"'{name}'").ToArray();
        return list.Length == 0 ? "no inputs" : string.Join(", ", list);
    }
}
