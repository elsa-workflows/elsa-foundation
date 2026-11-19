using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Builds the closed Jint value world shared by portable expressions and scripts.</summary>
internal static class IsolatedJintEngine
{
    public static Engine Create(FeatureOptions configured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var options = new JintOptions();

        if (configured.ExecutionTimeout is { } timeout && timeout > TimeSpan.Zero)
            options.TimeoutInterval(timeout);
        if (configured.MaxStatements is { } maxStatements && maxStatements > 0)
            options.MaxStatements(maxStatements);
        if (configured.MaxRecursionDepth is { } maxRecursionDepth && maxRecursionDepth > 0)
            options.LimitRecursion(maxRecursionDepth);
        if (configured.MaxMemoryBytes is { } maxMemoryBytes && maxMemoryBytes > 0)
            options.LimitMemory(maxMemoryBytes);
        if (configured.MaxArrayLength is { } maxArrayLength && maxArrayLength > 0)
            options.Constraints.MaxArraySize = maxArrayLength;
        if (cancellationToken.CanBeCanceled)
            options.CancellationToken(cancellationToken);

        var engine = new Engine(options);
        JintResultMaterializer.Initialize(engine);
        DisableAmbientCapabilities(engine);
        return engine;
    }

    /// <summary>
    /// Installs <c>args</c>: the declared parameters, entered through the value bridge as plain, changeable values (spec
    /// 1228 FR-001). Changing <c>args</c> is local to the evaluation: they are inputs, not variables, so a change is never
    /// a write. The <c>args</c> global itself cannot be replaced.
    /// </summary>
    public static void SetArgs(
        Engine engine,
        IScriptValueEvaluation values,
        IReadOnlyDictionary<string, JsonElement> parameters,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(configured);
        ValidateInput(parameters, configured);
        SetArgsCore(
            engine,
            values,
            parameters
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => (item.Key, item.Value)));
    }

    public static void SetArgs(
        Engine engine,
        IScriptValueEvaluation values,
        JsonElement arguments,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(configured);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("JavaScript script arguments must be a JSON object.");
        ValidateInput(arguments, configured);
        SetArgsCore(
            engine,
            values,
            arguments
                .EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => (property.Name, property.Value)));
    }

    private static void SetArgsCore(
        Engine engine,
        IScriptValueEvaluation values,
        IEnumerable<(string Name, JsonElement Value)> parameters)
    {
        var args = engine.Intrinsics.Object.Construct([]);
        foreach (var parameter in parameters)
        {
            args.DefineOwnProperty(
                parameter.Name,
                new PropertyDescriptor(values.Enter(ValuePaths.Member("args", parameter.Name), parameter.Value), writable: true, enumerable: true, configurable: true));
        }

        engine.Global.DefineOwnProperty("args", new PropertyDescriptor(args, writable: false, enumerable: true, configurable: false));
    }

    /// <summary>
    /// Installs the <c>variables</c> root and <c>getVariable</c> (spec 1222 FR-003, FR-008, ADR 0089). Names publish
    /// pinned read their pinned value; on an expression with a computed or opaque read, any other name goes through the
    /// host's <paramref name="reader"/>, which records the read. A value enters through the value bridge the first time it
    /// is read, as a plain, changeable value (spec 1228 FR-001), and every read path to one variable returns that same
    /// JavaScript value within the evaluation (FR-003, spec 1222 FR-034). An assignment to <c>variables.x</c> or
    /// <c>variables[name]</c> is recorded, and later reads see it. The returned handle reports, after the run, what the
    /// script changed. An unset declared variable reads JSON <c>null</c> (spec 1222 FR-006); a computed name that matches
    /// nothing reads <c>undefined</c> (FR-011); a refused read throws, and the host has already recorded the refusal, so
    /// catching it does not hide it (FR-017a). No generated <c>get&lt;Name&gt;()</c> accessor is installed (ADR 0089
    /// decision 6). With no pinned read and no reader nothing is installed, so an expression that reads no variable sees
    /// no <c>variables</c> surface, and <see langword="null"/> is returned.
    /// </summary>
    public static ScriptVariables? SetVariables(
        Engine engine,
        IScriptValueEvaluation values,
        IReadOnlyDictionary<string, JsonElement> pinned,
        IExpressionVariableReader? reader,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(configured);
        if (pinned.Count == 0 && reader is null)
            return null;
        ValidateInput(pinned, configured);

        var variables = new ScriptVariables(values);
        // Every value the script has been handed, pinned or looked up: the input limits apply to them together.
        var loaded = new Dictionary<string, JsonElement>(pinned, StringComparer.Ordinal);

        JsValue Read(string name)
        {
            if (variables.TryGet(name, out var known))
                return known;
            if (pinned.TryGetValue(name, out var element))
                return variables.Enter(name, element);
            // A name that only an engine probe asks for (JSON.stringify asks for toJSON, string conversion for toString
            // and valueOf, await for then) is not looked up when no variable has it: the engine cannot tell its own
            // probe from a script's, so such a name reads undefined unrecorded on every path. A variable that has the
            // name is a candidate, and reading it is recorded as usual.
            if (reader is null || (EngineProbes.Contains(name) && !reader.CandidateNames.Contains(name, StringComparer.Ordinal)))
                return JsValue.Undefined;

            var read = reader.Read(name);
            switch (read.Outcome)
            {
                case ExpressionVariableReadOutcome.Present:
                    loaded[name] = read.Value;
                    ValidateInput(loaded, configured);
                    return variables.Enter(name, read.Value);
                case ExpressionVariableReadOutcome.Refused:
                    throw new JavaScriptException(engine.Intrinsics.Error, read.Refusal);
                default:
                    variables.Missing(name);
                    return JsValue.Undefined;
            }
        }

        JsValue Assign(string name, JsValue value)
        {
            variables.Assign(name, value);
            return JsValue.Undefined;
        }

        // A null-prototype target: inherited members such as toString are not variables. Every pinned name and every
        // candidate is an own, enumerable accessor, so enumerating or spreading `variables` sees them; reading one goes
        // through Read, so it enters once and is recorded.
        var target = engine.Evaluate("Object.create(null)").AsObject();
        var names = pinned.Keys.Order(StringComparer.Ordinal).Concat(reader?.CandidateNames ?? []);
        foreach (var name in names)
        {
            if (target.HasOwnProperty(name))
                continue;
            var captured = name;
            variables.Declare(captured);
            var getter = new ClrFunction(engine, "get " + captured, (_, _) => Read(captured));
            var setter = new ClrFunction(engine, "set " + captured, (_, arguments) => Assign(captured, arguments.At(0)));
            target.DefineOwnProperty(captured, new GetSetPropertyDescriptor(getter, setter, enumerable: true, configurable: false));
        }

        target.PreventExtensions();
        // A name that is not an own property is still looked up, so the missing read is recorded with its warning, and
        // an assignment to it is recorded as a change. The engine's own probes (JSON.stringify asks for toJSON, string
        // conversion for toString and valueOf, await for then) are not reads a script made, so they read undefined
        // unrecorded.
        var miss = new ClrFunction(engine, "miss", (_, arguments) => Read(arguments.At(0).AsString()));
        var assign = new ClrFunction(engine, "assign", (_, arguments) => Assign(arguments.At(0).AsString(), arguments.At(1)));
        var factory = engine.Evaluate(
            "(function (target, miss, assign) {" +
            " var has = Function.prototype.call.bind(Object.prototype.hasOwnProperty); var get = Reflect.get; var set = Reflect.set;" +
            " return new Proxy(target, {" +
            "  get: function (t, p, r) { return typeof p === 'string' && !has(t, p) ? miss(p) : get(t, p, r); }," +
            "  set: function (t, p, v, r) { if (typeof p === 'string' && !has(t, p)) { assign(p, v); return true; } return set(t, p, v, r); }" +
            " });" +
            " })");
        var root = engine.Invoke(factory, target, miss, assign);

        engine.Global.DefineOwnProperty("variables", new PropertyDescriptor(root, writable: false, enumerable: true, configurable: false));

        var getVariable = new ClrFunction(engine, "getVariable", (_, callArguments) =>
            callArguments.Length >= 1 && callArguments[0].IsString()
                ? Read(callArguments[0].AsString())
                : JsValue.Undefined);
        engine.Global.DefineOwnProperty("getVariable", new PropertyDescriptor(getVariable, writable: false, enumerable: false, configurable: false));
        return variables;
    }

    /// <summary>The variables one evaluation read or assigned, and what it did to each.</summary>
    internal sealed class ScriptVariables(IScriptValueEvaluation values)
    {
        private readonly Dictionary<string, JsValue> _current = new(StringComparer.Ordinal);
        private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];
        private readonly HashSet<string> _declared = new(StringComparer.Ordinal);

        /// <summary>A name the evaluation can name: pinned, or a candidate of its lookup.</summary>
        public void Declare(string name) => _declared.Add(name);

        /// <summary>Whether a name is declared; any other name the script computed, possibly from a value.</summary>
        public bool IsDeclared(string name) => _declared.Contains(name);

        public bool TryGet(string name, out JsValue value) => _current.TryGetValue(name, out value!);

        public JsValue Enter(string name, JsonElement element)
        {
            var value = values.Enter(Key(name), element);
            Remember(name, value);
            return value;
        }

        public void Missing(string name) => Remember(name, JsValue.Undefined);

        public void Assign(string name, JsValue value)
        {
            Remember(name, value);
            _assigned.Add(name);
        }

        /// <summary>One change per variable read or assigned, in the order the script first touched them.</summary>
        public IReadOnlyList<(string Name, ScriptValueChange Change)> Collect() =>
            _order.Select(name => (name, CollectOne(name))).ToArray();

        // A value the script made unserializable (a cycle, a BigInt) or grew past the JSON limits (depth, size) was
        // changed: it is reported as changed as a whole, so an input expression fails with its write diagnostic rather
        // than the engine's or the parser's error (spec 1228 FR-006a).
        private ScriptValueChange CollectOne(string name)
        {
            try
            {
                return values.Collect(Key(name), _current[name], _assigned.Contains(name));
            }
            catch (Exception exception) when (exception is JavaScriptException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return new ScriptValueChange(Key(name), ScriptValueChangeKind.WholeAssignment, null, [Key(name)], []);
            }
        }

        public static string Key(string name) => ValuePaths.Member("variables", name);

        private void Remember(string name, JsValue value)
        {
            if (!_current.ContainsKey(name))
                _order.Add(name);
            _current[name] = value;
        }
    }

    private static readonly HashSet<string> EngineProbes = new(StringComparer.Ordinal)
    {
        "toJSON", "toString", "valueOf", "toLocaleString", "then", "constructor"
    };

    private static void DisableAmbientCapabilities(Engine engine)
    {
        // Time, randomness, locale, and environment data enter through pinned args, never intrinsics.
        foreach (var name in new[] { "Date", "Temporal", "Intl" })
            engine.Global.DefineOwnProperty(name, new PropertyDescriptor(JsValue.Undefined, writable: false, enumerable: false, configurable: false));

        engine.GetValue("Math").AsObject().DefineOwnProperty(
            "random",
            new PropertyDescriptor(JsValue.Undefined, writable: false, enumerable: false, configurable: false));
    }

    private static void ValidateInput(
        IReadOnlyDictionary<string, JsonElement> parameters,
        FeatureOptions configured)
    {
        ValidateInputOptions(configured);

        var nodeCount = 0;
        foreach (var parameter in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
            nodeCount = ValidateInputTree(parameter.Value, configured, depth: 1, nodeCount);

        ValidateInputBytes(configured, writer =>
        {
            writer.WriteStartObject();
            foreach (var parameter in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(parameter.Key);
                parameter.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        });
    }

    private static void ValidateInput(JsonElement arguments, FeatureOptions configured)
    {
        ValidateInputOptions(configured);
        _ = ValidateInputTree(arguments, configured, depth: 0, nodeCount: -1);
        ValidateInputBytes(configured, arguments.WriteTo);
    }

    private static void ValidateInputOptions(FeatureOptions configured)
    {
        ValidatePositive(configured.MaxInputBytes, nameof(configured.MaxInputBytes));
        ValidatePositive(configured.MaxInputDepth, nameof(configured.MaxInputDepth));
        ValidatePositive(configured.MaxInputNodes, nameof(configured.MaxInputNodes));
    }

    private static void ValidateInputBytes(FeatureOptions configured, Action<Utf8JsonWriter> write)
    {
        var countingBuffer = new BoundedJsonBufferWriter(configured.MaxInputBytes);
        using var writer = new Utf8JsonWriter(
            countingBuffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        write(writer);
        writer.Flush();
    }

    private static int ValidateInputTree(
        JsonElement element,
        FeatureOptions configured,
        int depth,
        int nodeCount)
    {
        if (depth > configured.MaxInputDepth)
        {
            throw new InvalidOperationException(
                $"A JavaScript parameter exceeds the configured JSON depth limit of {configured.MaxInputDepth}.");
        }

        nodeCount++;
        if (nodeCount > configured.MaxInputNodes)
        {
            throw new InvalidOperationException(
                $"JavaScript parameters exceed the configured aggregate limit of {configured.MaxInputNodes} nodes.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    nodeCount = ValidateInputTree(property.Value, configured, depth + 1, nodeCount);
                break;
            case JsonValueKind.Array:
                if (configured.MaxArrayLength is { } maxArrayLength &&
                    maxArrayLength > 0 &&
                    element.GetArrayLength() > maxArrayLength)
                {
                    throw new InvalidOperationException(
                        $"A JavaScript parameter array exceeds the configured limit of {maxArrayLength} items.");
                }

                foreach (var item in element.EnumerateArray())
                    nodeCount = ValidateInputTree(item, configured, depth + 1, nodeCount);
                break;
        }

        return nodeCount;
    }

    private static void ValidatePositive(int value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"JavaScript sandbox option '{name}' must be greater than zero.");
    }

    /// <summary>
    /// Gives <see cref="Utf8JsonWriter"/> bounded scratch space without retaining serialized bytes. Scratch
    /// allocation never exceeds the configured budget (or the writer's small minimum buffer), while
    /// <see cref="Advance"/> enforces the aggregate serialized-byte limit.
    /// </summary>
    private sealed class BoundedJsonBufferWriter(int maxBytes) : IBufferWriter<byte>
    {
        private const int DefaultBufferSize = 4096;
        private byte[] _buffer = new byte[Math.Min(maxBytes, DefaultBufferSize)];
        private int _written;

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (_written > maxBytes - count)
                throw NewLimitException();
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));

            var required = Math.Max(sizeHint, 1);
            // Utf8JsonWriter asks for a minimum-sized scratch buffer (currently 256 bytes) even when a
            // much smaller payload limit is configured. Permit bounded scratch space, while Advance
            // remains the authority for the aggregate byte budget.
            var maxScratchBytes = Math.Max(maxBytes, DefaultBufferSize);
            if (required > maxScratchBytes)
                throw NewLimitException();
            if (_buffer.Length < required)
                _buffer = new byte[required];
        }

        private InvalidOperationException NewLimitException() =>
            new($"JavaScript parameters exceed the configured aggregate limit of {maxBytes} bytes.");
    }
}
