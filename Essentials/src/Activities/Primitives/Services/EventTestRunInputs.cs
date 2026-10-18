using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

internal static class EventTestRunInputs
{
    public static TestRunTriggerValidation Validate(JsonElement? data) =>
        data is { } value && (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(property => property.Name != "payload"))
            ? TestRunTriggerValidation.Failure([new("invalid-event-configuration", "Event configuration accepts only a payload field.")])
            : TestRunTriggerValidation.Success();

    public static JsonElement? Payload(JsonElement? data) =>
        data is { } value && value.TryGetProperty("payload", out var supplied) ? supplied.Clone() : null;
}
