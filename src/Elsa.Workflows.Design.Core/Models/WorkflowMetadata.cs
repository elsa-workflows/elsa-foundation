namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// 
/// </summary>
/// <param name="Provider"></param>
/// <param name="Materializer"></param>
/// <param name="MaterializerContext"></param>
/// <param name="IsSystem"></param>
/// <param name="ToolVersion"></param>
/// <param name="CustomProperties">
///     Stores custom information about the workflow. Can be used to store application-specific properties to associate with the workflow.
/// </param>
public sealed record WorkflowMetadata(
    string Provider,
    string Materializer,
    string MaterializerContext,
    bool IsSystem,
    string? ToolVersion,    
    IDictionary<string, object> CustomProperties
);
