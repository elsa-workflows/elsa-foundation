using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes transient script-activity programs in the same closed value world as input bindings, with the same
/// intrinsics, clock and registered functions (spec 1223 FR-003).
/// </summary>
internal sealed class JintJavaScriptScriptEvaluator(
    IOptions<FeatureOptions> featureOptions,
    IScriptValueBridge valueBridge,
    IJavaScriptPrecisionPolicy precisionPolicy,
    JavaScriptHostFunctions hostFunctions,
    TimeProvider? timeProvider = null,
    ILogger<JintJavaScriptScriptEvaluator>? logger = null) : IJavaScriptScriptEvaluator
{
    public ValueTask<JavaScriptScriptEvaluationResult> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var options = featureOptions.Value;
        var engine = IsolatedJintEngine.Create(options, request.CancellationToken, timeProvider);
        using var values = valueBridge.Begin(engine);
        // A host function reads variables through the body's session, as it does through an input binding's reader
        // (spec 1223 FR-003, FR-029; spec 1222 T047), and sees what the script holds for a variable it already read or
        // changed, so every read path to one variable gives the same value within the run (spec 1222 FR-034).
        IsolatedJintEngine.ScriptVariables? variables = null;
        var functionReader = request.Variables is { } reader
            ? new CurrentVariableReader(reader, () => variables, engine, options)
            : null;
        var functions = new JavaScriptHostFunctionContext(values, functionReader, logger);
        hostFunctions.Install(engine, options, functions, request.CancellationToken);
        IsolatedJintEngine.SetArgs(engine, values, request.Arguments, options);
        // Spec 1222 FR-033: the body reads and writes the step's variables through its session; every read is recorded
        // there, literal or computed, so nothing is pinned into the engine.
        variables = request.Variables is { } session
            ? IsolatedJintEngine.SetVariables(engine, values, NoPinnedValues, session, options)
            : null;
        var completions = ScriptCompletions.Install(engine, request.CancellationToken);

        var returned = engine.Evaluate(PreparedJavaScript.Script(request.Source));

        // Only the script's own top-level returned value selects, wherever it was created (a `return f()` counts).
        JavaScriptScriptEvaluationResult result;
        IReadOnlyList<JavaScriptPrecisionLoss> losses;
        if (completions.TryGet(returned, out var completion))
        {
            var (state, names) = completion.ResolveSelection();
            result = new JavaScriptScriptEvaluationResult(Write(completions, values, completion.Result, out losses), state, names);
        }
        else
            result = new JavaScriptScriptEvaluationResult(Write(completions, values, returned, out losses));

        var warnings = new List<Elsa.Expressions.Core.Models.ExecutionWarning>(functions.Warnings);
        if (variables is not null)
            losses = [.. losses, .. JavaScriptValueWrites.WriteVariables(variables, request.Variables!, precisionPolicy, warnings)];

        // A value the script read through its session may be protected: its warnings then keep their protected form,
        // which names no member of it (spec 1229 FR-006). A protected input is handled by the step's own collector.
        var sink = request.Variables?.HasReadProtectedValue == true && request.Warnings is { } warningSink
            ? new ProtectingWarningSink(warningSink)
            : request.Warnings;
        foreach (var warning in warnings)
            sink?.Report(warning);
        JavaScriptValueWrites.WarnLossyReads(values, losses, sink, logger);
        return ValueTask.FromResult(result);
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> NoPinnedValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>
    /// A host function's view of the body's variables: the value the script holds now for a variable it has read or
    /// changed (its changes are written to the session only after the run), otherwise the session's value, read and
    /// recorded by the session.
    /// </summary>
    private sealed class CurrentVariableReader(
        IScriptVariableSession session,
        Func<IsolatedJintEngine.ScriptVariables?> current,
        Engine engine,
        FeatureOptions options) : Elsa.Expressions.Core.Contracts.IExpressionVariableReader
    {
        public IReadOnlyList<string> CandidateNames => session.CandidateNames;

        public Elsa.Expressions.Core.Contracts.ExpressionVariableRead Read(string name)
        {
            if (current() is not { } variables || !variables.TryGet(name, out var held))
                return session.Read(name);
            if (held.IsUndefined())
                return Elsa.Expressions.Core.Contracts.ExpressionVariableRead.Missing;
            return JintResultMaterializer.Stringify(engine, held) is { } json
                ? Elsa.Expressions.Core.Contracts.ExpressionVariableRead.Present(JintResultMaterializer.ParseResult(json, options))
                : throw new JavaScriptException(engine.Intrinsics.TypeError, $"Variable '{name}' holds a value with no JSON form.");
        }
    }

    // Reports each warning's protected form, when it has one.
    private sealed class ProtectingWarningSink(Elsa.Expressions.Core.Contracts.IExecutionWarningSink inner) : Elsa.Expressions.Core.Contracts.IExecutionWarningSink
    {
        public void Report(Elsa.Expressions.Core.Models.ExecutionWarning warning) => inner.Report(warning.ProtectedForm ?? warning);
    }

    /// <summary>The result leaves through the value bridge, and a lossy one meets the precision policy (spec 1228 FR-013).</summary>
    private JsonElement? Write(ScriptCompletions completions, IScriptValueEvaluation values, JsValue value, out IReadOnlyList<JavaScriptPrecisionLoss> losses)
    {
        var written = JavaScriptValueWrites.Apply(completions.Materialize(values, value), "result", precisionPolicy);
        losses = written.Losses;
        return written.Value;
    }

    /// <summary>
    /// The built-in <c>complete(result, outcomes)</c> of one evaluation (spec 1208 FR-007 to FR-007b). It is a pure
    /// constructor: a call snapshots its arguments into a frozen, empty object and changes nothing else. The brand is
    /// the object's identity in this evaluator-held table, which script data cannot forge: a plain object of the same
    /// shape is ordinary data. Outcomes are validated only when the value is returned, so an unreturned call never
    /// faults.
    /// </summary>
    internal sealed class ScriptCompletions
    {
        private readonly Dictionary<ObjectInstance, Completion> _issued = new(ReferenceEqualityComparer.Instance);
        private readonly CancellationToken _cancellationToken;

        private ScriptCompletions(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;
        private bool _materializing;

        public static ScriptCompletions Install(Engine engine, CancellationToken cancellationToken)
        {
            var completions = new ScriptCompletions(cancellationToken);
            var complete = new ClrFunction(engine, JavaScriptScriptBuiltIns.Complete, (_, arguments) => completions.Create(engine, arguments));
            engine.Global.DefineOwnProperty(
                JavaScriptScriptBuiltIns.Complete,
                new PropertyDescriptor(complete, writable: false, enumerable: false, configurable: false));
            return completions;
        }

        public bool TryGet(JsValue value, out Completion completion)
        {
            if (value is ObjectInstance instance && _issued.TryGetValue(instance, out completion!))
                return true;
            completion = null!;
            return false;
        }

        /// <summary>
        /// Materializes a result the way any returned value is (ADR 0046). A completion value reached inside it, nested
        /// in returned data or stored as a result, faults: it can select only as the top-level returned value.
        /// </summary>
        public ScriptValueWrite Materialize(IScriptValueEvaluation values, JsValue value)
        {
            if (value.IsUndefined())
                return new ScriptValueWrite(null, []);

            _materializing = true;
            try
            {
                return values.Write(value, "result");
            }
            finally
            {
                _materializing = false;
            }
        }

        private JsValue Create(Engine engine, JsValue[] arguments)
        {
            var result = arguments.Length > 0 ? arguments[0] : JsValue.Undefined;
            var outcomes = arguments.Length > 1 ? Snapshot(arguments[1], _cancellationToken) : OutcomeArgument.Omitted;

            var value = engine.Intrinsics.Object.Construct([]);
            // A completion value serializes only when it is not the top-level returned value; that is the nested case.
            var toJson = new ClrFunction(engine, "toJSON", (_, _) => _materializing
                ? throw new JavaScriptCompletionException(
                    $"A '{JavaScriptScriptBuiltIns.Complete}(...)' value selects outcomes only as the script's top-level returned value; it was found inside the returned data or result.")
                : JsValue.Undefined);
            value.DefineOwnProperty("toJSON", new PropertyDescriptor(toJson, writable: false, enumerable: false, configurable: false));
            value.PreventExtensions();
            _issued.Add(value, new Completion(result, outcomes));
            return value;
        }

        /// <summary>Copies the outcomes argument at call time, so later changes to a passed array change nothing.</summary>
        internal static OutcomeArgument Snapshot(JsValue outcomes, CancellationToken cancellationToken)
        {
            if (outcomes.IsUndefined())
                return OutcomeArgument.Omitted;
            if (outcomes.IsString())
                return new OutcomeArgument([outcomes.AsString()], IsList: false, Invalid: null);
            if (outcomes.IsArray())
            {
                var array = outcomes.AsObject();
                var length = TypeConverter.ToLength(array.Get("length"));
                var names = new List<string>((int)Math.Min(length, 1024));
                for (ulong index = 0; index < length; index++)
                {
                    // The copy runs outside Jint's statement limits, so it honours the evaluation's cancellation itself.
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = array.Get(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (!item.IsString())
                        return OutcomeArgument.Error($"outcome {index} is {Describe(item)}, not a string");
                    names.Add(item.AsString());
                }

                return new OutcomeArgument(names, IsList: true, Invalid: null);
            }

            return OutcomeArgument.Error(outcomes.IsNull()
                ? "null; omit the outcomes (or pass undefined) to leave the selection unspecified, or pass [] to select no path"
                : $"{Describe(outcomes)}, not a string or an array of strings");
        }

        private static string Describe(JsValue value) => value.IsNull() ? "null" : value.Type.ToString().ToLowerInvariant();
    }

    internal sealed record OutcomeArgument(IReadOnlyList<string> Names, bool IsList, string? Invalid)
    {
        public static OutcomeArgument Omitted { get; } = new([], IsList: false, Invalid: null);
        public static OutcomeArgument Error(string reason) => new([], IsList: false, reason);
    }

    internal sealed record Completion(JsValue Result, OutcomeArgument Outcomes)
    {
        public (JavaScriptOutcomeSelectionState State, IReadOnlyList<string> Names) ResolveSelection()
        {
            if (Outcomes.Invalid is { } reason)
                throw Invalid(reason);
            if (Outcomes.Names.Count == 0)
                return Outcomes.IsList
                    ? (JavaScriptOutcomeSelectionState.Empty, [])
                    : (JavaScriptOutcomeSelectionState.Unspecified, []);

            var names = new List<string>(Outcomes.Names.Count);
            foreach (var name in Outcomes.Names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw Invalid("an outcome name is blank");
                if (!names.Contains(name, StringComparer.Ordinal))
                    names.Add(name);
            }

            return (JavaScriptOutcomeSelectionState.Named, names);
        }

        private static JavaScriptCompletionException Invalid(string reason) => new(
            $"The outcomes passed to '{JavaScriptScriptBuiltIns.Complete}(result, outcomes)' are invalid: {reason}. Outcomes must be omitted, " +
            "undefined, an outcome name, or an array of outcome names.");
    }
}
