using System.Globalization;
using System.Net;
using System.Text.Json;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.Conversion;

internal sealed partial class Elsa3ActivityNormalizer
{
    private const string StatusListGuidance = "Set a nonempty ExpectedStatusCodes list in Elsa 3 and review its Done/status connections before converting.";

    private static bool HasSourceCorrections(Elsa3MemberRule rule) =>
        rule.OnAbsent is not null || rule.OnBlank is not null || rule.OnLiteralNull is not null ||
        rule.Contract is Elsa3ValueContract.HttpRequestContent;

    private static bool HasPotentialValue(Elsa3RewriteContext context, string member) =>
        context.Activity.AdditionalProperties.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Key, member) &&
            p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !IsDefaultedInput(p.Value) && !IsLiteralNullInput(p.Value));

    // These are the null wrapped-Input paths in the tagged loader, not arbitrary unbound values.
    private static bool IsDefaultedInput(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || source.EnumerateObject().Any(p => p.Name is not ("typeName" or "expression")))
            return false;
        if (source.TryGetProperty("typeName", out var type) && (type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString())))
            return false;
        if (!source.TryGetProperty("expression", out var expression))
            return true;
        return source.TryGetProperty("typeName", out _) && expression.ValueKind == JsonValueKind.Object && !expression.EnumerateObject().Any();
    }

    private static bool IsLiteralNullInput(JsonElement source) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty("typeName", out var declared) && declared.ValueKind == JsonValueKind.String &&
        source.TryGetProperty("expression", out var expression) &&
        expression.ValueKind == JsonValueKind.Object && expression.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String && StringComparer.OrdinalIgnoreCase.Equals(type.GetString(), "Literal") &&
        (!expression.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null);

    private static bool ApplySourceCorrection(Elsa3RewriteContext context, Elsa3MemberRule rule, string property,
        JsonElement source, Dictionary<string, JsonElement> result, Dictionary<string, string> boundBy)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return false; // The delivered bare-JSON-null table remains authoritative.
        if (IsDefaultedInput(source))
        {
            if (rule.OnAbsent is { } absent)
                ApplyNull(context, rule, absent, property, result, boundBy, "a defaulted empty input");
            return true;
        }
        if (IsLiteralNullInput(source))
        {
            if ((rule.OnLiteralNull ?? rule.OnNull) is { } literalNull)
                ApplyNull(context, rule, literalNull, property, result, boundBy);
            else
                ApplyRule(context, rule, property, JsonNull, result, boundBy);
            return true;
        }
        if (source.ValueKind == JsonValueKind.Object &&
            (!source.TryGetProperty("typeName", out var declared) || declared.ValueKind != JsonValueKind.String ||
             (source.TryGetProperty("expression", out _) || !Elsa3TypedValues.IsCaptured(source)) &&
             !Elsa3TypedValues.TryGetExpression(source, out _, out _)))
        {
            context.Error(ValueOutsideContract, $"Member '{property}' has unsupported input/expression metadata.",
                $"Set an explicit compatible value for '{property}' in the Elsa 3 workflow.", property);
            return true;
        }
        if (rule.OnBlank is { } blank && Elsa3TypedValues.TryGetLiteral(source, out var text) && string.IsNullOrWhiteSpace(text))
        {
            ApplyNull(context, rule, blank, property, result, boundBy, "blank");
            return true;
        }
        if (rule.Rule == Elsa3MemberRuleKind.DefaultFlip && !Elsa3TypedValues.IsBound(source))
        {
            context.Error(ValueOutsideContract, $"Member '{property}' contains an authored value whose legacy conversion is not established; it must not acquire the declared default.",
                $"Set an explicit compatible value for '{property}' in the Elsa 3 workflow.", property);
            return true;
        }
        return false;
    }

    // Run before IsBound: [], "null", and invalid literals are authored values, not defaults.
    private static bool CheckFocusedContract(Elsa3RewriteContext context, Elsa3MemberRule rule, string property,
        JsonElement value, out JsonElement? fitted)
    {
        fitted = value;
        if (value.ValueKind == JsonValueKind.Null)
            return false;
        var literal = Elsa3TypedValues.TryGetLiteral(value, out var text);
        switch (rule.Contract)
        {
            case Elsa3ValueContract.NonemptyHttpStatusCodes:
                if (!literal || !TryNonemptyStatusList(text))
                    fitted = Outside("must be a nonempty literal status list: Elsa 3 selects Done for every response when cleared, while Elsa 4 selects Failed for non-2xx responses",
                        StatusListGuidance);
                return true;
            case Elsa3ValueContract.PositiveLiteralTimeSpan:
                if (literal && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var interval) && interval > TimeSpan.Zero)
                    fitted = Elsa3TypedValues.CreateLiteral("String", interval.ToString("c", CultureInfo.InvariantCulture));
                else
                    fitted = Outside("must be a positive literal interval; null, blank, invalid and nonpositive legacy values must not become one minute",
                        "Set Interval to a positive literal time span such as 00:05:00 in the Elsa 3 workflow.");
                return true;
            case Elsa3ValueContract.PositiveHttpResponseStatus:
                if (literal && Enum.TryParse<HttpStatusCode>(text, true, out var status) && (int)status > 0)
                    fitted = Elsa3TypedValues.CreateLiteral("Int32", ((int)status).ToString(CultureInfo.InvariantCulture));
                else
                    fitted = Outside("must be a positive literal HTTP status: Elsa 3 assigns the raw status, while Elsa 4 replaces values below one with 200; a computed range is not proven by its declared type",
                        "Set StatusCode to an explicit compatible positive status in the Elsa 3 workflow.");
                return true;
            case Elsa3ValueContract.HttpRequestContent:
                if (literal && text == string.Empty && HasNonNullContentType(context))
                    fitted = Outside("is empty String content: Elsa 3 attaches an empty body and content type, while Elsa 4 omits the body and its headers",
                        "Remove the empty Content or supply nonempty text in the Elsa 3 workflow, and review the receiving endpoint.");
                else if (!literal && value.ValueKind != JsonValueKind.Null)
                    fitted = Outside("must be literal String content whose body presence is known; a computed or referenced String may be empty, in which case Elsa 4 omits the body and its headers",
                        "Set Content to compatible nonempty literal text in the Elsa 3 workflow, or rebuild and review the HTTP request in Elsa 4.");
                return true;
            default:
                return false;
        }

        JsonElement? Outside(string reason, string guidance)
        {
            context.Error(ValueOutsideContract, $"Input '{property}' {reason}.", guidance, property);
            return null;
        }
    }

    private static bool HasNonNullContentType(Elsa3RewriteContext context) =>
        context.Activity.AdditionalProperties.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Key, "ContentType") &&
            p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !IsDefaultedInput(p.Value) && !IsLiteralNullInput(p.Value));

    private static bool TryNonemptyStatusList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.ValueKind == JsonValueKind.Array && json.RootElement.GetArrayLength() > 0 &&
                   json.RootElement.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
