namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// 
/// </summary>
/// <param name="Provider"></param>
/// <param name="Materializer"></param>
/// <param name="MaterializerContext"></param>
/// <param name="IsSystem"></param>
/// <param name="ToolVersion"></param>
public sealed record WorkflowMetadata(
    string? Materializer,
    string? MaterializerContext,
    bool IsSystem,
    string? ToolVersion
);
