using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Exact model-owned declarations and capabilities for one supplied enclosing scope.</summary>
public sealed record RuntimeTestScopeContract(string ScopeId, VariableFrameKind Kind, bool IsClosedBoundary,
    IReadOnlyDictionary<string, RuntimeVariableDeclaration> Variables,
    IReadOnlyDictionary<string, ActivityInputContract> Inputs, bool Writable);
