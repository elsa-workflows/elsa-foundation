namespace Elsa.Expressions.JavaScript.Jint;

/// <summary>
/// One entry of the declared script-sandbox surface. <see cref="Kind"/> is one of:
/// <c>function</c> (a host-installed global function), <c>frozenObject</c> (a host-installed frozen data object),
/// <c>perVariableAccessor</c> (a name pattern installed once per workflow variable), or
/// <c>disabledIntrinsic</c> (a standard JavaScript capability the sandbox deliberately removes).
/// </summary>
public sealed record SandboxGlobalDescriptor(string Name, string Kind, string? Signature = null, string? Availability = null);

/// <summary>
/// The declared Jint script-sandbox surface (spec 149 / RFC #1191): the globals
/// <see cref="Services.IsolatedJintEngine"/> installs imperatively, published as structural data so the
/// contract fragment of this assembly can describe the sandbox without executing an engine.
/// A guard test (<c>SandboxSurfaceCatalogTests</c>) composes a real engine and asserts the catalog and the
/// installed surface cannot drift.
/// </summary>
public static class SandboxSurfaceCatalog
{
    public static IReadOnlyList<SandboxGlobalDescriptor> Globals { get; } =
    [
        new("args", "frozenObject",
            Signature: "const args: Readonly<Record<string, any>>",
            Availability: "Installed when the expression declares parameters; frozen (non-writable, non-extensible)."),
        new("variables", "frozenObject",
            Signature: "const variables: Readonly<Record<string, any>>",
            Availability: "Installed when the workflow exposes visible variables; frozen (non-writable, non-extensible)."),
        new("getVariable", "function",
            Signature: "getVariable(name: string): any",
            Availability: "Installed when the workflow exposes visible variables; returns undefined for unknown names."),
        new("get{VariableName}", "perVariableAccessor",
            Signature: "get{PascalCaseVariableName}(): any",
            Availability: "One accessor per visible variable whose name is a bare JavaScript identifier."),
        new("Date", "disabledIntrinsic",
            Availability: "Undefined by design: time enters through pinned args, never ambient capability."),
        new("Temporal", "disabledIntrinsic",
            Availability: "Undefined by design: time enters through pinned args, never ambient capability."),
        new("Intl", "disabledIntrinsic",
            Availability: "Undefined by design: locale data enters through pinned args, never ambient capability."),
        new("Math.random", "disabledIntrinsic",
            Availability: "Undefined by design: randomness enters through pinned args, never ambient capability.")
    ];
}
