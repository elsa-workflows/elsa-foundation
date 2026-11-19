using System.Text.Json;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// The implementation of a .NET-implemented JavaScript function (spec 1223 FR-011, ADR 0045 §5 D-5). Every argument and
/// the result are JSON values of the core value model: script values map to JSON as a script result does (a
/// <c>Date</c> becomes its ISO 8601 UTC text, <c>undefined</c> becomes <c>null</c>), and no live .NET object crosses.
/// Returning a <see cref="JsonElement"/> of kind <see cref="JsonValueKind.Undefined"/> returns <c>undefined</c>. A thrown
/// exception fails the call with a JavaScript error that carries its message.
/// </summary>
public delegate JsonElement JavaScriptHostFunction(JavaScriptHostFunctionCall call);

/// <summary>One call of a .NET-implemented JavaScript function.</summary>
/// <param name="Arguments">The arguments, as JSON values.</param>
/// <param name="Variables">
/// The evaluation's variable lookup, when it has one. A function declared with
/// <see cref="JavaScriptHostFunctionDeclaration.ReadsVariables"/> reads through it, so its reads are recorded, checked and
/// snapshotted like any computed read (spec 1222 FR-008).
/// </param>
/// <param name="CancellationToken">The evaluation's cancellation.</param>
public sealed record JavaScriptHostFunctionCall(
    IReadOnlyList<JsonElement> Arguments,
    IExpressionVariableReader? Variables,
    CancellationToken CancellationToken);

/// <summary>
/// Registers a .NET-implemented function that JavaScript expressions and scripts may call (spec 1222 FR-008, spec 1223
/// FR-011, FR-012, FR-027, FR-029). Register one per function in the service collection, as a singleton. The engine
/// installs it on both surfaces, input bindings and <c>RunJavaScript</c> scripts, and registering it marks it allowed in
/// <c>binding-readonly-v1</c> (ADR 0083).
/// <para>
/// <b>Name.</b> A host function carries a namespace prefix, <c>acme.lookup</c>: it is installed as member <c>lookup</c>
/// of a read-only global object <c>acme</c>. Only the built-ins of <see cref="JavaScriptBuiltInFunctions"/> are
/// unprefixed. A duplicate name, a namespace root that is reserved (<see cref="JavaScriptBuiltInFunctions.IsReservedRoot"/>)
/// or a malformed name fails startup with a message naming the function.
/// </para>
/// <para>
/// <b>Reads.</b> A function that reads workflow variables the script does not name in source must say so with
/// <see cref="ReadsVariables"/>: publish then sets the fallback marker for every expression that calls it, so the
/// variables it could read are made readable before the script runs (ADR 0089 R3).
/// </para>
/// <para>
/// <b>Effects.</b> None: every registered function is effect-free under ADR 0045 §5 D-6, and calling one adds no commit
/// boundary. The effectful declaration ships with the boundary that acts on it (#4831).
/// </para>
/// </summary>
/// <param name="Name">The name the script calls: <c>namespace.member</c>, or a built-in's own name.</param>
/// <param name="Implementation">The .NET implementation; JSON values in and out.</param>
/// <param name="ReadsVariables">Whether the function reads workflow variables (an opaque read).</param>
public sealed record JavaScriptHostFunctionDeclaration(string Name, JavaScriptHostFunction Implementation, bool ReadsVariables = false)
{
    public string Name { get; } = !string.IsNullOrWhiteSpace(Name)
        ? Name
        : throw new ArgumentException("A host function declaration needs a name.", nameof(Name));

    public JavaScriptHostFunction Implementation { get; } = Implementation
        ?? throw new ArgumentNullException(nameof(Implementation), $"Host function '{Name}' needs an implementation.");
}
