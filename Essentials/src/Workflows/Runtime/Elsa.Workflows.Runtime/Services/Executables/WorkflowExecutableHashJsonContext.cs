using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>Fixed serializer metadata for the v2 binding hash contract; no host converters participate.</summary>
// Match the behavioral payload writer's default depth instead of imposing the serializer's lower 64-level limit.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, MaxDepth = 1000)]
[JsonSerializable(typeof(RuntimeInputBinding))]
internal sealed partial class WorkflowExecutableHashJsonContext : JsonSerializerContext;
