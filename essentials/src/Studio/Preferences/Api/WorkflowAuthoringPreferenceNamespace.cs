using System.Text.Json;
using Elsa.Studio.Preferences.Core;

namespace Elsa.Studio.Preferences.Api;

internal sealed class WorkflowAuthoringPreferenceNamespace : ObjectPreferenceNamespace
{
    public override string Name => "workflow-authoring";
    public override int MaxBytes => 1024;

    public override bool Validate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var properties = value.EnumerateObject();
        if (!properties.MoveNext())
            return false;
        var property = properties.Current;
        return property.NameEquals("editorId") && property.Value.ValueKind == JsonValueKind.String
            && property.Value.GetString() is { Length: > 0 and <= 128 } id
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            && !properties.MoveNext();
    }
}
