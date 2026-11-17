using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Executes transient script-activity programs in the same closed value world as pure bindings.</summary>
internal sealed class JintJavaScriptScriptEvaluator(IOptions<FeatureOptions> featureOptions) : IJavaScriptScriptEvaluator
{
    public ValueTask<JavaScriptScriptEvaluationResult> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var options = featureOptions.Value;
        var engine = IsolatedJintEngine.Create(options, request.CancellationToken);
        IsolatedJintEngine.SetReadOnlyArgs(engine, request.Arguments, options);
        var completions = ScriptCompletions.Install(engine, request.CancellationToken);

        var returned = engine.Evaluate(PreparedJavaScript.Script(request.Source));

        // Only the script's own top-level returned value selects, wherever it was created (a `return f()` counts).
        if (completions.TryGet(returned, out var completion))
        {
            var (state, names) = completion.ResolveSelection();
            return ValueTask.FromResult(new JavaScriptScriptEvaluationResult(
                completions.Materialize(engine, completion.Result, options), state, names));
        }

        return ValueTask.FromResult(new JavaScriptScriptEvaluationResult(completions.Materialize(engine, returned, options)));
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
        public JsonElement? Materialize(Engine engine, JsValue value, FeatureOptions options)
        {
            if (value.IsUndefined())
                return null;

            _materializing = true;
            try
            {
                return JintResultMaterializer.Materialize(engine, value, options);
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
