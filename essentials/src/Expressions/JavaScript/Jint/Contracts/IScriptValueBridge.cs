using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Models;
using Jint;
using Jint.Native;

namespace Elsa.Expressions.JavaScript.Jint.Contracts;

/// <summary>
/// Puts values into a JavaScript evaluation and finds what the evaluation changed (ADR 0046 V9, V12; spec 1228 FR-001,
/// FR-003 to FR-005, FR-009). Script activities and JavaScript input expressions use the same bridge (D-15).
/// Replacement contract: exactly one implementation is active per host. The default is copy, compare, patch. A
/// replacement that behaves identically keeps the JSON profile; one that behaves differently is a new profile.
/// </summary>
/// <remarks>
/// It lives with the Jint engine, not in the engine-neutral JavaScript core, because entering a value and finding a
/// change both work on the engine's values.
/// </remarks>
[Elsa.Expressions.JavaScript.Core.Contracts.JavaScriptValueReplacementContract]
public interface IScriptValueBridge
{
    /// <summary>Starts the values of one evaluation in <paramref name="engine"/>. Dispose it when the evaluation ends.</summary>
    IScriptValueEvaluation Begin(Engine engine);
    /// <summary>Starts an evaluation with the selected executable's undefined policy.</summary>
    IScriptValueEvaluation Begin(Engine engine, bool undefinedAsNull);
}

/// <summary>The values of one evaluation: what entered, and what leaves.</summary>
public interface IScriptValueEvaluation : IDisposable
{
    IReadOnlyList<Elsa.Expressions.Core.Models.ExecutionWarning> Warnings { get; }
    /// <summary>
    /// Converts a value entering the evaluation into a plain, changeable JavaScript value (objects, real arrays), and
    /// records it under <paramref name="key"/> (such as <c>variables.order</c>) with the numbers it cannot hold exactly.
    /// </summary>
    JsValue Enter(string key, JsonElement value);

    /// <summary>
    /// After the run, the change to one value: <paramref name="current"/> is what the script holds under
    /// <paramref name="key"/> now, and <paramref name="assigned"/> says whether the script assigned it.
    /// </summary>
    ScriptValueChange Collect(string key, JsValue current, bool assigned);

    /// <summary>
    /// During the run, the change <see cref="Collect"/> would report now, so a host function reading a variable gets the
    /// value the session will hold (spec 1222 FR-034): the stored JSON when unchanged, untouched members keeping their
    /// stored text. Nothing is remembered, since the script can still change the value: no unchanged judgement is cached
    /// and nothing counts as written exactly.
    /// </summary>
    ScriptValueChange Preview(string key, JsValue current, bool assigned);

    /// <summary>
    /// Converts a value leaving the evaluation, such as a result, to JSON. An entered value that is unchanged is written
    /// from its original JSON. Reports the lossy entered numbers the written JSON carries (FR-011a).
    /// </summary>
    ScriptValueWrite Write(JsValue value, string path);

    /// <summary>Every lossy number that entered the evaluation, in entry order.</summary>
    IReadOnlyList<JavaScriptLossyNumber> LossyNumbers { get; }

    /// <summary>
    /// The lossy entered numbers written from their original JSON so far, inside an unchanged value that was written:
    /// they keep every digit, so they are neither a loss nor a warning (FR-011a, FR-015).
    /// </summary>
    IReadOnlyCollection<JavaScriptLossyNumber> WrittenExactly { get; }

    /// <summary>
    /// Whether <paramref name="value"/> is, as a double, a lossy entered number. A host function checks its arguments
    /// with it and logs a warning without refusing the call (FR-015).
    /// </summary>
    bool TryFindLoss(double value, out JavaScriptLossyNumber? number);
}
