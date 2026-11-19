using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
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
    /// <summary>
    /// A fresh engine with the native intrinsics, <c>Date</c>, <c>Intl</c>, <c>Temporal</c> and <c>Math.random</c>
    /// included (spec 1223 FR-004, FR-005), on the host clock in UTC with the invariant culture (FR-002).
    /// </summary>
    public static Engine Create(FeatureOptions configured, CancellationToken cancellationToken, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var options = new JintOptions
        {
            TimeSystem = new JintTimeSystem(timeProvider ?? TimeProvider.System),
            TimeZone = TimeZoneInfo.Utc,
            Culture = CultureInfo.InvariantCulture,
        };
        // Intl and Temporal take their default zone from the Temporal zone provider, not TimeZone: UTC there too.
        options.Temporal.TimeZoneProvider = UtcDefaultTimeZoneProvider.Instance;

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
        DefaultIntlToUtc(engine);
        return engine;
    }

    /// <summary>
    /// Jint's <c>Intl.DateTimeFormat</c> formats in the host's local zone when no <c>timeZone</c> is given, whatever the
    /// engine's zone. Every expression evaluates in UTC with no ambient zone (spec 1223 FR-002, FR-023), so the
    /// constructor defaults <c>timeZone</c> to <c>UTC</c>; an explicit zone is kept. Installed before user code runs.
    /// </summary>
    private static void DefaultIntlToUtc(Engine engine) => engine.Execute(IntlUtcDefault);

    // Parsed once: every evaluation's engine runs the same wrapper.
    private static readonly Prepared<Acornima.Ast.Script> IntlUtcDefault = Engine.PrepareScript(
        "(function () {" +
        " var Native = Intl.DateTimeFormat;" +
        " function withUtc(options) {" +
        "  if (options === undefined) return { timeZone: 'UTC' };" +
        "  var o = Object(options); return o.timeZone === undefined ? Object.assign({}, o, { timeZone: 'UTC' }) : options;" +
        " }" +
        " function DateTimeFormat(locales, options) { return new Native(locales, withUtc(options)); }" +
        " DateTimeFormat.prototype = Native.prototype;" +
        " Object.defineProperty(DateTimeFormat, 'supportedLocalesOf', { value: Native.supportedLocalesOf, writable: true, configurable: true });" +
        " Object.defineProperty(Native.prototype, 'constructor', { value: DateTimeFormat, writable: true, configurable: true });" +
        " Object.defineProperty(Intl, 'DateTimeFormat', { value: DateTimeFormat, writable: true, configurable: true, enumerable: false });" +
        "})();");

    public static void SetReadOnlyArgs(
        Engine engine,
        IReadOnlyDictionary<string, JsonElement> parameters,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(configured);
        ValidateInput(parameters, configured);
        SetReadOnlyArgsCore(
            engine,
            parameters
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => (item.Key, item.Value)));
    }

    public static void SetReadOnlyArgs(
        Engine engine,
        JsonElement arguments,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(configured);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("JavaScript script arguments must be a JSON object.");
        ValidateInput(arguments, configured);
        SetReadOnlyArgsCore(
            engine,
            arguments
                .EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => (property.Name, property.Value)));
    }

    private static void SetReadOnlyArgsCore(
        Engine engine,
        IEnumerable<(string Name, JsonElement Value)> parameters)
    {
        var args = engine.Intrinsics.Object.Construct([]);
        foreach (var parameter in parameters)
        {
            args.DefineOwnProperty(
                parameter.Name,
                new PropertyDescriptor(CreateValue(engine, parameter.Value), writable: false, enumerable: true, configurable: false));
        }

        args.PreventExtensions();
        engine.Global.DefineOwnProperty("args", new PropertyDescriptor(args, writable: false, enumerable: true, configurable: false));
    }

    /// <summary>
    /// Installs the <c>variables</c> root and <c>getVariable</c> (spec 1222 FR-003, FR-008, ADR 0089). Names publish
    /// pinned read their pinned value; on an expression with a computed or opaque read, any other name goes through the
    /// host's <paramref name="reader"/>, which records the read. Every read path to one variable returns the same
    /// JavaScript value within the evaluation (FR-034), and the values are frozen (FR-037). An unset declared variable
    /// reads JSON <c>null</c> (FR-006); a computed name that matches nothing reads <c>undefined</c> (FR-011); a refused
    /// read throws, and the host has already recorded the refusal, so catching it does not hide it (FR-017a). No
    /// generated <c>get&lt;Name&gt;()</c> accessor is installed (ADR 0089 decision 6, FR-036). With no pinned read and
    /// no reader nothing is installed, so an expression that reads no variable sees no <c>variables</c> surface.
    /// </summary>
    public static void SetReadOnlyVariables(
        Engine engine,
        IReadOnlyDictionary<string, JsonElement> pinned,
        IExpressionVariableReader? reader,
        FeatureOptions configured)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(configured);
        if (pinned.Count == 0 && reader is null)
            return;
        ValidateInput(pinned, configured);

        var values = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        // Every value the script has been handed, pinned or looked up: the input limits apply to them together.
        var loaded = new Dictionary<string, JsonElement>(pinned, StringComparer.Ordinal);
        foreach (var (name, element) in pinned.OrderBy(item => item.Key, StringComparer.Ordinal))
            values[name] = CreateValue(engine, element);

        JsValue Read(string name)
        {
            if (values.TryGetValue(name, out var known))
                return known;
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
                    var value = CreateValue(engine, read.Value);
                    values[name] = value;
                    return value;
                case ExpressionVariableReadOutcome.Refused:
                    throw new JavaScriptException(engine.Intrinsics.Error, read.Refusal);
                default:
                    values[name] = JsValue.Undefined;
                    return JsValue.Undefined;
            }
        }

        // A null-prototype target: inherited members such as toString are not variables.
        var target = engine.Evaluate("Object.create(null)").AsObject();
        foreach (var name in values.Keys.Order(StringComparer.Ordinal).ToArray())
            target.DefineOwnProperty(name, new PropertyDescriptor(values[name], writable: false, enumerable: true, configurable: false));
        if (reader is not null)
        {
            // Every candidate is an own, enumerable property, so enumerating or spreading `variables` sees them; reading
            // one goes through the host and is recorded.
            foreach (var name in reader.CandidateNames)
            {
                if (target.HasOwnProperty(name))
                    continue;
                var captured = name;
                var getter = new ClrFunction(engine, "get " + captured, (_, _) => Read(captured));
                target.DefineOwnProperty(captured, new GetSetPropertyDescriptor(getter, JsValue.Undefined, enumerable: true, configurable: false));
            }
        }

        target.PreventExtensions();
        JsValue root = target;
        if (reader is not null)
        {
            // A name that is not a candidate is still looked up, so the missing read is recorded with its warning. The
            // engine's own probes (JSON.stringify asks for toJSON, string conversion for toString and valueOf, await for
            // then) are not reads a script made, so they read undefined unrecorded.
            var miss = new ClrFunction(engine, "miss", (_, arguments) => Read(arguments.At(0).AsString()));
            var factory = engine.Evaluate(
                "(function (target, miss) {" +
                " var has = Function.prototype.call.bind(Object.prototype.hasOwnProperty); var get = Reflect.get;" +
                " return new Proxy(target, { get: function (t, p, r) { return typeof p === 'string' && !has(t, p) ? miss(p) : get(t, p, r); } });" +
                " })");
            root = engine.Invoke(factory, target, miss);
        }

        engine.Global.DefineOwnProperty("variables", new PropertyDescriptor(root, writable: false, enumerable: true, configurable: false));

        var getVariable = new ClrFunction(engine, "getVariable", (_, callArguments) =>
            callArguments.Length >= 1 && callArguments[0].IsString()
                ? Read(callArguments[0].AsString())
                : JsValue.Undefined);
        engine.Global.DefineOwnProperty("getVariable", new PropertyDescriptor(getVariable, writable: false, enumerable: false, configurable: false));
    }

    private static readonly HashSet<string> EngineProbes = new(StringComparer.Ordinal)
    {
        "toJSON", "toString", "valueOf", "toLocaleString", "then", "constructor"
    };

    private static JsValue CreateValue(Engine engine, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => CreateObject(engine, element),
        JsonValueKind.Array => CreateArray(engine, element),
        JsonValueKind.String => JsValue.FromObject(engine, element.GetString()),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => JsValue.FromObject(engine, integer),
        JsonValueKind.Number => JsValue.FromObject(engine, element.GetDouble()),
        JsonValueKind.True => JsBoolean.True,
        JsonValueKind.False => JsBoolean.False,
        JsonValueKind.Null => JsValue.Null,
        _ => throw new InvalidOperationException($"JSON kind '{element.ValueKind}' is not a persistable JavaScript parameter value.")
    };

    private static JsValue CreateObject(Engine engine, JsonElement element)
    {
        var value = engine.Intrinsics.Object.Construct([]);
        foreach (var property in element.EnumerateObject())
        {
            value.DefineOwnProperty(
                property.Name,
                new PropertyDescriptor(CreateValue(engine, property.Value), writable: false, enumerable: true, configurable: false));
        }

        value.PreventExtensions();
        return value;
    }

    private static JsValue CreateArray(Engine engine, JsonElement element)
    {
        var length = element.GetArrayLength();
        var value = engine.Intrinsics.Array.Construct((uint)length);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            value.DefineOwnProperty(
                index.ToString(CultureInfo.InvariantCulture),
                new PropertyDescriptor(CreateValue(engine, item), writable: false, enumerable: true, configurable: false));
            index++;
        }

        value.DefineOwnProperty("length", new PropertyDescriptor(JsNumber.Create(length), writable: false, enumerable: false, configurable: false));
        value.PreventExtensions();
        return value;
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
