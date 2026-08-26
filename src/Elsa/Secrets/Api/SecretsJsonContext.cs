using Elsa.Secrets.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Secrets.Api;

/// <summary>
/// The owner's source-generated serializer context handed to the module endpoint group. The
/// operations currently perform their own serialization over the module's runtime-configured
/// options (camel-cased string enums), so this context only mirrors that shape for the group.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecretMetadata))]
internal partial class SecretsJsonContext : JsonSerializerContext;
