using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Finds JavaScript that relies on live .NET objects, which the default JSON-only engine strategy does not hand to
/// expressions (spec 1223 FR-016 to FR-019, ADR 0045 §5 D-5, ADR 0046 V10). It parses the source and never evaluates it.
/// Publish validation calls it for every script; the Elsa 3 converter calls it with the values it knows were .NET
/// objects in Elsa 3. It belongs to the default strategy: the opt-in .NET interop strategy (#4103) decides whether it
/// applies there.
/// <para>
/// Refused: a reference to a .NET type (static access or construction) whose name is in the analyzer's catalog and is
/// not a JavaScript global; an invocation of a PascalCase member on a value the script did not define; and, with a
/// <see cref="JavaScriptDotNetUsageContext"/>, member use on a known .NET-object value. Allowed: property reads of any
/// casing (JSON keys), invocations on a JavaScript global or a registered function namespace, members the script
/// defines, names the script declares, and computed member access, which is not classified (a recorded gap).
/// </para>
/// </summary>
public interface IJavaScriptDotNetUsageAnalyzer
{
    /// <summary>Analyzes <paramref name="source"/>.</summary>
    /// <param name="source">The JavaScript.</param>
    /// <param name="scriptBody">Whether the source is a script-activity program (top-level <c>return</c> allowed).</param>
    /// <param name="context">At conversion, the values known to have been live .NET objects; <see langword="null"/> at publish.</param>
    /// <returns>The refusals, in source order, or <see langword="null"/> when the source does not parse.</returns>
    IReadOnlyList<JavaScriptDotNetUsage>? Analyze(string source, bool scriptBody = false, JavaScriptDotNetUsageContext? context = null);
}

/// <summary>What a value was in Elsa 3, when the converter knows it was a live .NET object.</summary>
public enum JavaScriptDotNetValueKind
{
    /// <summary>A wrapped .NET object (for example a <c>FormFile</c> or a <c>Stream</c>): no member is available.</summary>
    Object,

    /// <summary>A .NET <c>Guid</c>: only <c>toString()</c> with no arguments gives the same text.</summary>
    Guid,

    /// <summary>A .NET list or dictionary, which behaved as a JavaScript array or object: native members are available.</summary>
    Collection,
}

/// <summary>
/// The values a converted script reads that were live .NET objects in Elsa 3 (spec 1223 FR-016), keyed by their Elsa 4
/// read path (<c>variables.upload</c>, <c>args.file</c>). The results of <c>newGuid()</c> and <c>parseGuid(...)</c> are
/// known GUIDs whenever a context is given. Direct use and <c>const</c>/<c>let</c> aliases are followed.
/// </summary>
/// <param name="KnownValues">The read paths of known .NET-object values and what each was.</param>
public sealed record JavaScriptDotNetUsageContext(IReadOnlyDictionary<string, JavaScriptDotNetValueKind> KnownValues);

/// <summary>One refused use of a .NET object or type.</summary>
/// <param name="Expression">The offending expression, quoted from the source.</param>
/// <param name="Message">Why it is refused and how to rewrite it.</param>
/// <param name="Range">Where the expression is, zero-based, for an editor; diagnostics quote <paramref name="Expression"/>.</param>
public sealed record JavaScriptDotNetUsage(string Expression, string Message, ExpressionToolingRange Range);
