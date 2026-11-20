using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class BasicUserTaskForms : IUserTaskFormProvider
{
    public string Provider => UserTaskProtocol.BasicForms;

    public void ValidateDefinition(UserTaskForm form)
    {
        if (form.Provider != Provider || string.IsNullOrWhiteSpace(form.Id) || string.IsNullOrWhiteSpace(form.Version) ||
            string.IsNullOrWhiteSpace(form.Name) || form.Id.Length > 256 || form.Version.Length > 128 || form.Fields is null || form.Fields.Any(x => x is null) || form.Fields.Count > 100 ||
            form.Fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != form.Fields.Count)
            throw Invalid("The form identity, version and unique fields are required (maximum 100 fields).");
        foreach (var field in form.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key) || field.Key.Length > 256 || string.IsNullOrWhiteSpace(field.Label) ||
                field.Kind is not ("text" or "multiline" or "number" or "boolean" or "date" or "choice") ||
                field.MaxLength is < 1 or > 100000 || field.Minimum > field.Maximum ||
                (field.MaxLength is not null && field.Kind is not ("text" or "multiline")) ||
                ((field.Minimum is not null || field.Maximum is not null) && field.Kind != "number") ||
                (field.Kind == "choice" && (field.Choices is not { Count: > 0 } || field.Choices.Any(string.IsNullOrWhiteSpace) ||
                    field.Choices.Distinct(StringComparer.Ordinal).Count() != field.Choices.Count)))
                throw Invalid("A form field has an invalid key, label, kind, constraint or choice list.");
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
            var has = response.TryGetProperty(field.Key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            if (has && value.ValueKind == JsonValueKind.String && field.MaxLength is { } maximum && value.GetString()!.Length > maximum)
                errors[field.Key] = "Enter a value matching this field's type and constraints.";
            if (!has || (field.Kind is "text" or "multiline" && value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
            {
                if (field.Required && requireFields) errors[field.Key] = "This field is required.";
                continue;
            }
            var valid = field.Kind switch
            {
                "text" or "multiline" => value.ValueKind == JsonValueKind.String && (field.MaxLength is null || value.GetString()!.Length <= field.MaxLength),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) &&
                    (field.Minimum is null || number >= field.Minimum) && (field.Maximum is null || number <= field.Maximum),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "choice" => value.ValueKind == JsonValueKind.String && field.Choices!.Contains(value.GetString()!, StringComparer.Ordinal),
                _ => false
            };
            if (!valid) errors[field.Key] = "Enter a value matching this field's type and constraints.";
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
}
