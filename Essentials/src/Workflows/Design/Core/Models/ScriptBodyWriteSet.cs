namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The variables one script body may write (spec 1231 FR-019, FR-020a): <paramref name="Variables"/>, each resolved to
/// its declaring scope, or every visible variable when <paramref name="MayWriteEveryVisibleVariable"/> is set.
/// </summary>
public sealed record ScriptBodyWriteSet(IReadOnlyList<ScriptBodyWriteTarget> Variables, bool MayWriteEveryVisibleVariable);

/// <summary>A variable a script body may write: its name, the scope that declares it (<c>workflow</c> for the root) and its reference key.</summary>
public sealed record ScriptBodyWriteTarget(string Name, string ScopeId, string ReferenceKey);
