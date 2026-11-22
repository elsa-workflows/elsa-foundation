using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class BasicUserTaskForms : IUserTaskFormProvider
{
    private const int ConfigurationSizeLimit = 16_384;
    private const int ConfigurationDepthLimit = 16;
    private readonly IUserTaskFieldControlValidator[] _fieldControls;

    public BasicUserTaskForms() : this([]) { }

    public BasicUserTaskForms(IEnumerable<IUserTaskFieldControlValidator> fieldControls) => _fieldControls = fieldControls.ToArray();

    public string Provider => UserTaskProtocol.BasicForms;

    public void ValidateDefinition(UserTaskForm form)
    {
        if (form.Provider != Provider || string.IsNullOrWhiteSpace(form.Id) || string.IsNullOrWhiteSpace(form.Version) ||
            string.IsNullOrWhiteSpace(form.Name) || form.Id.Length > 256 || form.Version.Length > 128 || form.Description is { Length: > 2000 } || form.Fields is null || form.Fields.Any(x => x is null) || form.Fields.Count > 100 ||
            form.Fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != form.Fields.Count)
            throw Invalid("The form identity, version and unique fields are required (maximum 100 fields).");
        foreach (var field in form.Fields)
        {
            var custom = field.ControlId is null ? null : FindFieldControl(field.ControlId);
            if (string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 256 || string.IsNullOrWhiteSpace(field.Label) ||
                field.Kind is null || field.Kind.Length > 64 ||
                (custom is null && field.Kind is not ("text" or "multiline" or "number" or "boolean" or "date" or "choice")) ||
                (custom is null && field.Configuration is not null) ||
                field.MaxLength is < 1 or > 100000 || field.Minimum > field.Maximum ||
                (custom is null && field.MaxLength is not null && field.Kind is not ("text" or "multiline")) ||
                (custom is null && (field.Minimum is not null || field.Maximum is not null) && field.Kind != "number") ||
                (custom is null && field.MustBeTrue && field.Kind != "boolean") ||
                (custom is not null && (field.Kind != custom.ValueKind || field.MaxLength is not null || field.Minimum is not null || field.Maximum is not null ||
                    field.Choices is not null || field.Presentation is not null || field.MustBeTrue)) ||
                (field.ControlId is { Length: > 128 }) || !ValidConfiguration(field.Configuration) ||
                (field.Presentation is { } presentation &&
                    ((field.Kind == "boolean" ? presentation.Display is not ("checkbox" or "dropdown" or "radio") :
                        field.Kind == "choice" ? presentation.Display is not ("dropdown" or "radio") : true) ||
                     (field.Kind != "boolean" && (presentation.TrueLabel is not null || presentation.FalseLabel is not null || presentation.UnansweredLabel is not null)) ||
                     new[] { presentation.TrueLabel, presentation.FalseLabel, presentation.UnansweredLabel }.Any(label =>
                         label is { Length: > 256 } || (label is not null && string.IsNullOrWhiteSpace(label))))) ||
                (field.Kind == "choice" && (field.Choices is not { Count: > 0 } || field.Choices.Any(string.IsNullOrWhiteSpace) ||
                    field.Choices.Distinct(StringComparer.Ordinal).Count() != field.Choices.Count)))
                throw Invalid("A form field has an invalid key, label, kind, constraint or choice list.");
            if (custom is not null)
            {
                var errors = custom.ValidateDefinition(field);
                if (errors.Count != 0) throw Invalid(string.Join(" ", errors));
            }
        }
    }

    public ValueTask<IReadOnlyDictionary<string, string>> ValidateResponseAsync(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidateResponse(form, prefill, response, requireFields));
    }

    public IReadOnlyDictionary<string, string> ValidateResponse(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields)
    {
        ValidateDefinition(form);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response.ValueKind != JsonValueKind.Object) { errors[""] = "Enter an object of form values."; return errors; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in response.EnumerateObject())
            if (!names.Add(property.Name) || !form.Fields.Any(x => x.Key == property.Name)) errors[property.Name] = "Unknown or duplicated field.";
        foreach (var field in form.Fields)
        {
            var custom = field.ControlId is null ? null : FindFieldControl(field.ControlId);
            var has = response.TryGetProperty(field.Key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            var emptyString = has && value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
            if (field.MustBeTrue && requireFields && (!has || value.ValueKind != JsonValueKind.True))
            {
                errors[field.Key] = "This field must be checked.";
                continue;
            }
            if (has && value.ValueKind == JsonValueKind.String && field.MaxLength is { } maximum && value.GetString()!.Length > maximum)
                errors[field.Key] = "Enter a value matching this field's type and constraints.";
            if (!has || (requireFields && field.Required && emptyString) || (field.ControlId is not null && emptyString) ||
                (field.Kind is "text" or "multiline" && emptyString))
            {
                if (field.Required && requireFields) errors[field.Key] = "This field is required.";
                continue;
            }
            var customError = custom is null ? null : custom.ValidateResponse(field, value);
            var valid = custom is not null
                ? ValueMatchesKind(custom.ValueKind, value) && customError is null
                : field.Kind switch
            {
                "text" or "multiline" => value.ValueKind == JsonValueKind.String && (field.MaxLength is null || value.GetString()!.Length <= field.MaxLength),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) &&
                    (field.Minimum is null || number >= field.Minimum) && (field.Maximum is null || number <= field.Maximum),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "choice" => value.ValueKind == JsonValueKind.String && field.Choices!.Contains(value.GetString()!, StringComparer.Ordinal),
                _ => false
            };
            if (!valid) errors[field.Key] = customError ?? "Enter a value matching this field's type and constraints.";
            if (field.ReadOnly && (!prefill.TryGetProperty(field.Key, out var initial) || !JsonElement.DeepEquals(initial, value)))
                errors[field.Key] = "This value cannot be changed.";
        }
        // Omitting a read-only prefilled value must not turn an immutable input into absent output.
        foreach (var field in form.Fields.Where(x => x.ReadOnly))
            if (field.Required && (!prefill.TryGetProperty(field.Key, out var required) || required.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                (required.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(required.GetString()))))
                errors[field.Key] = "The workflow must prefill this required read-only field.";
            else if (prefill.TryGetProperty(field.Key, out var initial) && (!response.TryGetProperty(field.Key, out var actual) || !JsonElement.DeepEquals(initial, actual)))
                errors[field.Key] = "This value cannot be changed.";
        return errors;
    }

    private static UserTaskException Invalid(string message) => new("validation", message);

    private IUserTaskFieldControlValidator FindFieldControl(string id)
    {
        var matches = _fieldControls.Where(control => string.Equals(control.Id, id, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : throw Invalid($"Field control “{id}” is unavailable or ambiguous.");
    }

    private static bool ValueMatchesKind(string kind, JsonElement value) => kind switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "any" => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined),
        _ => false
    };

    private static bool ValidConfiguration(JsonElement? configuration)
    {
        if (configuration is null) return true;
        var value = configuration.Value;
        return value.ValueKind == JsonValueKind.Object && value.GetRawText().Length <= ConfigurationSizeLimit && JsonDepth(value, 0) <= ConfigurationDepthLimit;
    }

    private static int JsonDepth(JsonElement value, int depth) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(property => JsonDepth(property.Value, depth + 1)).DefaultIfEmpty(depth).Max(),
        JsonValueKind.Array => value.EnumerateArray().Select(item => JsonDepth(item, depth + 1)).DefaultIfEmpty(depth).Max(),
        _ => depth
    };
}
