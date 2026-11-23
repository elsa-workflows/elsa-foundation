using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// The stable diagnostic codes of script variable reads (spec 1222, data model "Diagnostics"). <c>VF-READ-001</c>, a
/// computed name that matches nothing at run time, is recorded on the input snapshot's computed read. No code is
/// reserved for a possibly protected or transient candidate: an authored variable declaration can be neither today, so a
/// computed read is checked for the variable it actually reads, at run time.
/// </summary>
public static class ScriptVariableReadDiagnostics
{
    /// <summary>A literal name resolves to no visible declaration (FR-012, FR-023): publication is refused.</summary>
    public const string UnresolvedLiteralRead = "VF-READ-002";

    /// <summary>A literal name resolves to a transient variable (FR-017): publication is refused.</summary>
    public const string TransientLiteralRead = "VF-READ-003";

    /// <summary>A script reads variables by a computed or opaque name; the message lists its candidates (FR-009).</summary>
    public const string ComputedReadReported = "VF-READ-004";

    /// <summary>A literal name now resolves to a different declaration than in the published version (FR-024).</summary>
    public const string ShadowChange = "VF-READ-005";

    /// <summary>
    /// An input expression writes a variable (spec 1228 FR-006a): publication is refused. The same code fails the
    /// evaluation at run time for a write publication cannot see (<c>JavaScriptValueDiagnostics.InputExpressionWrite</c>).
    /// </summary>
    public const string InputExpressionWrite = "VF-BIND-001";

    /// <summary>A script body writes, by a literal name, a variable no visible scope declares (spec 1231 FR-020): publication is refused.</summary>
    public const string UndeclaredLiteralWrite = "VF-WRITE-002";

    /// <summary>A script body writes a transient variable by a literal name (spec 1231 FR-020): publication is refused.</summary>
    public const string TransientLiteralWrite = "VF-WRITE-003";

    /// <summary>A script body assigns, or assigns into, a loop item or index by a literal name (spec 1231 FR-020): publication is refused.</summary>
    public const string ReadOnlyLiteralWrite = "VF-WRITE-004";

    /// <summary>
    /// A script body writes variables by a computed or opaque name (spec 1231 FR-020a): it publishes, and the node may
    /// write every visible variable. A warning, so the author sees why the concurrent-write rule counts it everywhere.
    /// </summary>
    public const string ComputedWriteReported = "VF-WRITE-005";

    /// <summary>The refusal of one literal write in a script body (spec 1231 FR-020): the code, the position and the target.</summary>
    public static string DescribeLiteralWriteRefusal(string code, string inputKey, string nodeId, string source, ScriptVariableWrite write) =>
        $"{code}: the script of input '{inputKey}' on activity node '{nodeId}' writes variable '{write.Name}' ({write.Path}) at {Position(source, write.Start)}, " +
        code switch
        {
            UndeclaredLiteralWrite => "but no variable with that name is visible from the node. A script never creates a variable: declare it on the workflow or an enclosing container, or correct the name.",
            TransientLiteralWrite => "which is transient: a script cannot write it. Make the variable durable or write it from a C# activity.",
            ReadOnlyLiteralWrite => "which is a loop item or index and is read-only. Copy it into a declared variable and change that.",
            _ => "which is refused."
        };

    /// <summary>The warning for a script body that writes variables by a computed or opaque name (spec 1231 FR-020a).</summary>
    public static string DescribeComputedWrite(string inputKey, string nodeId) =>
        $"{ComputedWriteReported}: the script of input '{inputKey}' on activity node '{nodeId}' writes variables by a computed name, or uses 'variables', " +
        "'getVariable' or 'setVariable' as a value, so it may write every visible variable. Each name is resolved when the script runs; an undeclared " +
        "or transient name faults the step. Write variables by a literal name (variables.total = …) to keep the write set exact.";

    /// <summary>A one-based line and column of a zero-based source offset, or "an unknown position".</summary>
    public static string Position(string source, int offset)
    {
        if (offset < 0 || offset > source.Length)
            return "an unknown position";
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
                column++;
        }

        return $"line {line}, column {column}";
    }

    /// <summary>The diagnostic text of <see cref="InputExpressionWrite"/>, the same at publish and at run time.</summary>
    public static string DescribeInputExpressionWrite(string variable, string path) =>
        $"{InputExpressionWrite}: the input expression changes variable '{variable}' at '{path}'. " +
        "Input expressions only read; use a script activity (Run JavaScript) to change a variable, or change a local copy.";
}
