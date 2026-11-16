using System.Text.Json;
using Elsa.Activities.Runtime.Core.Serialization;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The JSON options for activity values crossing from one activity to the next. The completion projector writes
/// results with them and the input hydrator reads inputs with them, so a .NET object one activity returns reads back
/// with the same members in the next activity's input (#3799).
/// </summary>
/// <remarks>
/// Numbers stay strict: the Web defaults would read a JSON string such as <c>"42"</c> into a numeric input, which
/// the hydrator refused before and still refuses. S-9 (#3805) replaces this instance with the shared serializer
/// baseline.
/// </remarks>
internal static class ActivityValueJson
{
    public static JsonSerializerOptions Options { get; } = ActivityValueJsonProfile.CreateOptions();
}
