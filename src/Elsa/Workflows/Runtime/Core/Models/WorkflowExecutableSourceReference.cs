namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Foreign-key style source reference for diagnostics and migration tooling. Runtime execution does not load it.
/// </summary>
public sealed record WorkflowExecutableSourceReference(
    string SourceKind,
    string SourceId,
    string? SourceVersion = null);