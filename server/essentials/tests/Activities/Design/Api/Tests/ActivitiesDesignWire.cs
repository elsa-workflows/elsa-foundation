using System.Reflection;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>The Activities Design API's owner serialization context, bound to the effective wire options.</summary>
internal static class ActivitiesDesignWire
{
    public static JsonSerializerContext Context { get; } = (JsonSerializerContext)typeof(ActivitiesDesignApiFeature).Assembly
        .GetType("Elsa.Activities.Design.Api.ActivitiesDesignJsonOptions", throwOnError: true)!
        .GetProperty("WireContext", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;
}
