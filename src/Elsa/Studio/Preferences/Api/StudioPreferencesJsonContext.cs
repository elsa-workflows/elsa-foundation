using Elsa.Studio.Preferences.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Studio.Preferences.Api;

/// <summary>
/// The owner's source-generated serializer context handed to the module endpoint group. The
/// operations currently perform their own serialization over the host's HTTP JSON options, so this
/// context only mirrors that shape for the group.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StudioPreferenceDocument))]
internal partial class StudioPreferencesJsonContext : JsonSerializerContext;
