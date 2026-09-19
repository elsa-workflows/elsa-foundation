using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa3.Models;

public sealed class Elsa3WorkflowDefinition
{
    public string Id { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; }
    public string ToolVersion { get; set; } = null!;

    public IList<Elsa3Variable> Variables { get; set; } = [];
    public IList<Elsa3WorkflowArgumentDefinition> Inputs { get; set; } = [];
    public IList<Elsa3WorkflowArgumentDefinition> Outputs { get; set; } = [];
    public IList<string> Outcomes { get; set; } = [];
    public IDictionary<string, JsonElement> CustomProperties { get; set; }
        = new Dictionary<string, JsonElement>();

    public bool IsReadonly { get; set; }
    public bool IsSystem { get; set; }
    public bool IsLatest { get; set; }
    public bool IsPublished { get; set; }

    public Elsa3WorkflowOptions Options { get; set; } = new();
    public Elsa3Activity Root { get; set; } = null!;
}

public sealed class Elsa3WorkflowArgumentDefinition
{
    public string? UiHint { get; set; }

    public string? StorageDriverType { get; set; }

    [JsonRequired]
    public string Type { get; set; } = string.Empty;

    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }
}

public sealed class Elsa3WorkflowOptions
{
    public bool? UsableAsActivity { get; set; }
    public bool? AutoUpdateConsumingWorkflows { get; set; }
}

public sealed class Elsa3Variable
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string TypeName { get; set; } = null!;
    public bool IsArray { get; set; }
    public string? Value { get; set; }
    public string? StorageDriverTypeName { get; set; }
}

/// <summary>
/// Uniform shape for every activity — leaf or container. Type-specific parameters
/// (statusCode, content, path, cases, …) land in <see cref="AdditionalProperties"/>;
/// the mapper resolves them by looking up the activity definition's input/output
/// names and indexing into AdditionalProperties.
///
/// Container kinds (Flowchart, Sequence, …) populate <see cref="Activities"/> and
/// <see cref="Connections"/>; leaf kinds leave them null.
/// </summary>
public sealed class Elsa3Activity
{
    public string Id { get; set; } = null!;
    public string NodeId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public int? Version { get; set; }

    public Elsa3ActivityCustomProperties? CustomProperties { get; set; }

    public Elsa3ActivityMetadata? Metadata { get; set; }

    /// <summary>Child activities — present on container kinds (Flowchart etc.), null otherwise.</summary>
    public IList<Elsa3Activity>? Activities { get; set; }

    /// <summary>Edges between child activities — present on flowcharts, null otherwise.</summary>
    public IList<Elsa3Connection>? Connections { get; set; }

    /// <summary>
    /// Everything not modelled above (statusCode, content, contentType, path,
    /// supportedMethods, cases, mode, workflowDefinitionId, schema, ...). Each value
    /// is typically the <see cref="TypedValue"/> shape — deserialize on demand.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
        = new Dictionary<string, JsonElement>();
}

public sealed class Elsa3ActivityCustomProperties
{
    public bool CanStartWorkflow { get; set; }

    public bool RunAsynchronously { get; set; }
}

public sealed class Elsa3ActivityMetadata
{
    public Elsa3DesignerMetadata Designer { get; set; } = new();
    public string? DisplayText { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class Elsa3DesignerMetadata
{
    public Elsa3Position Position { get; set; } = new();
    public Elsa3Size Size { get; set; } = new();
}

public sealed class Elsa3Position { public double X { get; set; } public double Y { get; set; } }
public sealed class Elsa3Size { public double Width { get; set; } public double Height { get; set; } }

public sealed class Elsa3Connection
{
    public Elsa3Endpoint Source { get; set; } = null!;
    public Elsa3Endpoint Target { get; set; } = null!;
}

public sealed class Elsa3Endpoint
{
    public string Activity { get; set; } = null!;
    public string Port { get; set; } = null!;
}

/// <summary>
/// Shape used by every typed parameter on an activity:
///   { "typeName": "...", "expression": { ... }, "memoryReference"?: { "id": "..." } }
/// Input parameters carry an expression; output parameters carry only a memoryReference;
/// input+output bindings carry both.
/// </summary>
public sealed class Elsa3TypedValue
{
    public string TypeName { get; set; } = null!;
    public Elsa3Expression? Expression { get; set; }
    public Elsa3MemoryReference? MemoryReference { get; set; }
}

public sealed class Elsa3Expression
{
    public string Type { get; set; } = null!;   // "JavaScript", "Literal", "Object"
    public string Value { get; set; } = null!;
}

public sealed class Elsa3MemoryReference
{
    public string Id { get; set; } = null!;
}
