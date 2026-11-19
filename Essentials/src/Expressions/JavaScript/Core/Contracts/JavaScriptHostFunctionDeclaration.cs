namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Declares a host function a JavaScript script may call (spec 1222 FR-008, task T023). Register one per function in
/// the service collection, as a singleton. A function that reads workflow variables the script does not name in source
/// must say so with <see cref="ReadsVariables"/>: publish then sets the fallback marker for every expression that calls
/// it, so the variables it could read are made readable before the script runs. Its reads must go through the request's
/// <c>IExpressionVariableReader</c>, which records, checks and snapshots them like any computed read. No built-in host
/// function reads variables, and the isolated engine cannot install host functions yet: until it can, a declaration
/// only affects publish.
/// </summary>
/// <param name="Name">The global name the script calls.</param>
/// <param name="ReadsVariables">Whether the function reads workflow variables (an opaque read).</param>
public sealed record JavaScriptHostFunctionDeclaration(string Name, bool ReadsVariables = false)
{
    public string Name { get; } = !string.IsNullOrWhiteSpace(Name)
        ? Name
        : throw new ArgumentException("A host function declaration needs a name.", nameof(Name));
}
