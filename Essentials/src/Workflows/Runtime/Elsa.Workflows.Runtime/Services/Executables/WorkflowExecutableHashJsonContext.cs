using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Fixed serializer metadata for the v2 binding hash contract; no host converters participate.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RuntimeInputBinding))]
internal sealed partial class WorkflowExecutableHashJsonContext : JsonSerializerContext;
