using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The default script value bridge: copy, compare, patch (spec 1228 research R-1). Each value read enters as a plain,
/// changeable JavaScript value built from its JSON; the JSON is kept, with the numbers a double cannot hold exactly.
/// After the run each value is compared with what entered, JavaScript with JavaScript (ADR 0090 C2): numbers compare as
/// doubles and object members in any order. An unchanged value is never written, and an unchanged value that is written
/// elsewhere (returned, or placed inside another value) is written from its original JSON. A changed object reports a
/// member change whose untouched members keep their stored text; a changed array is written whole.
/// </summary>
internal sealed class CopyComparePatchValueBridge(IOptions<FeatureOptions> options) : IScriptValueBridge
{
    public IScriptValueEvaluation Begin(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new Evaluation(engine, options.Value);
    }

    private sealed class Evaluation : IScriptValueEvaluation
    {
        private const char MarkerStart = '';
        private readonly Engine _engine;
        private readonly FeatureOptions _options;
        private readonly Dictionary<ObjectInstance, JsonElement> _origins = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ObjectInstance, string> _originPaths = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, string> _markerPaths = new(StringComparer.Ordinal);
        private readonly List<JavaScriptLossyNumber> _exact = [];
        private readonly Dictionary<ObjectInstance, bool> _unchanged = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly List<JavaScriptLossyNumber> _lossy = [];
        private readonly Dictionary<double, JavaScriptLossyNumber> _lossyByValue = [];
        private readonly string _markerPrefix = MarkerStart + Guid.NewGuid().ToString("N") + ":";
        private JsValue? _replacer;
        private JsValue? _infinityReplacer;
        private bool _enteredInfinity;
        private bool _wroteInfinity;
        private long _enterTicks;
        private long _collectTicks;
        private long _bytes;
        private bool _disposed;

        public Evaluation(Engine engine, FeatureOptions options)
        {
            _engine = engine;
            _options = options;
        }

        public IReadOnlyList<JavaScriptLossyNumber> LossyNumbers => _lossy;

        public JsValue Enter(string key, JsonElement value)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var entered = Create(value, key);
                _entries[key] = new Entry(value, entered);
                _bytes += JsonMarshal.GetRawUtf8Value(value).Length;
                return entered;
            }
            finally
            {
                _enterTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        public ScriptValueChange Collect(string key, JsValue current, bool assigned)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (assigned)
                {
                    var whole = WriteCore(current, key);
                    return new ScriptValueChange(key, ScriptValueChangeKind.WholeAssignment, whole.Value, [key], whole.Losses);
                }

                // A value never read, or a primitive (which only an assignment can change), is unchanged.
                if (!_entries.TryGetValue(key, out var entry) || current is not ObjectInstance instance || IsUnchanged(instance))
                    return ScriptValueChange.Unchanged(key);

                if (entry.Element.ValueKind != JsonValueKind.Object || !current.IsObject() || current.IsArray())
                {
                    // FR-004: a changed array is written as a whole.
                    var array = WriteCore(current, key);
                    return new ScriptValueChange(key, ScriptValueChangeKind.WholeAssignment, array.Value, [key], array.Losses);
                }

                var marked = Mark(current);
                var changed = new List<string>();
                var losses = new List<JavaScriptPrecisionLoss>();
                var buffer = new ArrayBufferWriter<byte>();
                using (var writer = NewWriter(buffer))
                {
                    Patch(entry.Element, marked.Root, key, writer, changed, losses, marked.Markers);
                }

                var patched = JintResultMaterializer.ParseResult(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan), _options);
                _bytes += buffer.WrittenCount;
                return new ScriptValueChange(key, ScriptValueChangeKind.MemberChange, patched, changed, losses);
            }
            finally
            {
                _collectTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        public ScriptValueChange Preview(string key, JsValue current, bool assigned)
        {
            // Collect itself, remembering nothing: a later change by the script, or the real Collect, judges afresh.
            _previewing = true;
            try
            {
                return Collect(key, current, assigned);
            }
            finally
            {
                _previewing = false;
            }
        }

        private bool _previewing;

        // Lossy numbers written from their original JSON; a preview writes nothing, so it records none.
        private void WroteExactly(string path)
        {
            if (!_previewing)
                _exact.AddRange(_lossy.Where(number => IsWithin(number.Path, path)));
        }

        public ScriptValueWrite Write(JsValue value, string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            var started = Stopwatch.GetTimestamp();
            try
            {
                return WriteCore(value, path);
            }
            finally
            {
                _collectTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        public bool TryFindLoss(double value, out JavaScriptLossyNumber? number) => _lossyByValue.TryGetValue(value, out number);

        public IReadOnlyCollection<JavaScriptLossyNumber> WrittenExactly => _exact;

        private static bool IsWithin(string path, string prefix) =>
            path == prefix ||
            (path.StartsWith(prefix, StringComparison.Ordinal) && path.Length > prefix.Length && path[prefix.Length] is '.' or '[');

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            JavaScriptValueBridgeMetrics.Record(
                Stopwatch.GetElapsedTime(0, _enterTicks),
                Stopwatch.GetElapsedTime(0, _collectTicks),
                _bytes);
        }

        private ScriptValueWrite WriteCore(JsValue value, string path)
        {
            if (value.IsUndefined())
                return new ScriptValueWrite(null, []);

            var marked = Mark(value);
            var losses = new List<JavaScriptPrecisionLoss>();
            var buffer = new ArrayBufferWriter<byte>();
            _wroteInfinity = false;
            using (var writer = NewWriter(buffer))
            {
                WriteMarked(marked.Root, path, arrayPath: null, writer, losses, marked.Markers);
            }

            // The serialized value is returned as is only when nothing in it was replaced: no marker, no loss and no
            // Infinity, which is written as null even when no lossy entered number has its sign.
            var json = marked.Markers.Count == 0 && losses.Count == 0 && !_wroteInfinity
                ? marked.Root
                : JintResultMaterializer.ParseResult(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan), _options);
            _bytes += buffer.WrittenCount;
            return new ScriptValueWrite(json, losses);
        }

        /// <summary>
        /// Serializes a value, replacing each unchanged entered object or array met on the way down by a marker string
        /// that names its original JSON. The markers carry a per-evaluation nonce, so script data cannot forge one.
        /// </summary>
        private (JsonElement Root, Dictionary<string, JsonElement> Markers) Mark(JsValue value)
        {
            var markers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            // A value with no object in it (a number, a string) cannot hold an entered value: no replacer is needed, unless
            // an entered number became Infinity, which JSON writes as null and so must be marked to be found.
            if (!_enteredInfinity && (value is not ObjectInstance || _origins.Count == 0))
            {
                var plain = JintResultMaterializer.Stringify(_engine, value)
                    ?? throw new InvalidOperationException("A JavaScript result must be representable as persistable JSON.");
                return (JintResultMaterializer.ParseResult(plain, _options), markers);
            }

            _replacer ??= NewReplacer();
            // Restored, not cleared: a toJSON can call a host function whose variable read previews another value.
            var outer = _currentMarkers;
            _currentMarkers = markers;
            try
            {
                var text = JintResultMaterializer.Stringify(_engine, value, _replacer)
                    ?? throw new InvalidOperationException("A JavaScript result must be representable as persistable JSON.");
                return (JintResultMaterializer.ParseResult(text, _options), markers);
            }
            finally
            {
                _currentMarkers = outer;
            }
        }

        private Dictionary<string, JsonElement>? _currentMarkers;

        private JsValue NewReplacer()
        {
            var lookup = new ClrFunction(_engine, "lookup", (_, arguments) =>
            {
                var candidate = arguments.At(0);
                if (_currentMarkers is null ||
                    candidate is not ObjectInstance instance ||
                    !_origins.TryGetValue(instance, out var origin) ||
                    !IsUnchanged(instance))
                    return candidate;
                var marker = _markerPrefix + _currentMarkers.Count.ToString(CultureInfo.InvariantCulture);
                _currentMarkers[marker] = origin;
                _markerPaths[marker] = _originPaths[instance];
                return new JsString(marker);
            });
            // Plain operators only, so nothing the script changed on the prototypes can intercept the replacer.
            var factory = _engine.Evaluate(
                "(function (lookup, infinity) { return function (key, value) {" +
                " if (value === Infinity || value === -Infinity) return infinity(value);" +
                " return value !== null && typeof value === 'object' ? lookup(value) : value; }; })");
            return _engine.Invoke(factory, lookup, InfinityMarker());
        }

        /// <summary>
        /// JSON writes <c>Infinity</c> as <c>null</c>. A number that entered as a value too large for a double holds
        /// <c>Infinity</c>, so on the way out it is marked: an unchanged one still compares equal to what entered, and one
        /// that is written is a loss (spec 1228 FR-011: a number that becomes Infinity is lossy).
        /// </summary>
        private ClrFunction InfinityMarker() =>
            new(_engine, "infinity", (_, arguments) => new JsString(InfinityMarkerText(arguments.At(0).AsNumber())));

        private string InfinityMarkerText(double value) => _markerPrefix + (value > 0 ? "+inf" : "-inf");

        private bool TryReadInfinity(JsonElement element, out double value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.String)
                return false;
            var text = element.GetString()!;
            if (StringComparer.Ordinal.Equals(text, InfinityMarkerText(double.PositiveInfinity)))
                value = double.PositiveInfinity;
            else if (StringComparer.Ordinal.Equals(text, InfinityMarkerText(double.NegativeInfinity)))
                value = double.NegativeInfinity;
            else
                return false;
            return true;
        }

        private JsValue InfinityOnlyReplacer() =>
            _infinityReplacer ??= _engine.Invoke(
                _engine.Evaluate("(function (infinity) { return function (key, value) { return value === Infinity || value === -Infinity ? infinity(value) : value; }; })"),
                InfinityMarker());

        private bool IsUnchanged(ObjectInstance instance)
        {
            if (_previewing)
                return ComputeUnchanged(instance);
            if (_unchanged.TryGetValue(instance, out var known))
                return known;

            var unchanged = ComputeUnchanged(instance);
            if (_origins.ContainsKey(instance))
                _unchanged[instance] = unchanged;
            return unchanged;
        }

        private bool ComputeUnchanged(ObjectInstance instance)
        {
            if (!_origins.TryGetValue(instance, out var origin))
                return false;

            var text = _enteredInfinity
                ? JintResultMaterializer.Stringify(_engine, instance, InfinityOnlyReplacer())
                : JintResultMaterializer.Stringify(_engine, instance);
            bool unchanged;
            if (text is null)
                unchanged = false;
            else
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = Math.Max(_options.MaxResultDepth, _options.MaxInputDepth) + 1 });
                unchanged = SameJavaScriptValue(origin, document.RootElement, null);
            }

            return unchanged;
        }

        private JsValue Create(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var value = _engine.Intrinsics.Object.Construct(Arguments.Empty);
                    foreach (var property in element.EnumerateObject())
                        value.DefineOwnProperty(property.Name, new PropertyDescriptor(Create(property.Value, ValuePaths.Member(path, property.Name)), writable: true, enumerable: true, configurable: true));
                    _origins[value] = element;
                    _originPaths[value] = path;
                    return value;
                }
                case JsonValueKind.Array:
                {
                    var value = _engine.Intrinsics.Array.Construct(Arguments.Empty);
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        value.DefineOwnProperty(index.ToString(CultureInfo.InvariantCulture), new PropertyDescriptor(Create(item, ValuePaths.Index(path, index)), writable: true, enumerable: true, configurable: true));
                        index++;
                    }

                    _origins[value] = element;
                    _originPaths[value] = path;
                    return value;
                }
                case JsonValueKind.String:
                    return new JsString(element.GetString()!);
                case JsonValueKind.Number:
                    return CreateNumber(element.GetRawText(), path);
                case JsonValueKind.True:
                    return JsBoolean.True;
                case JsonValueKind.False:
                    return JsBoolean.False;
                case JsonValueKind.Null:
                    return JsValue.Null;
                default:
                    throw new InvalidOperationException($"JSON kind '{element.ValueKind}' is not a persistable JavaScript parameter value.");
            }
        }

        private JsValue CreateNumber(string text, string path)
        {
            if (!JavaScriptNumberPrecision.IsExact(text, out var held, out var isInteger))
            {
                var lossy = new JavaScriptLossyNumber(path, text, JavaScriptNumberPrecision.JavaScriptText(held), held, isInteger);
                _lossy.Add(lossy);
                _lossyByValue.TryAdd(held, lossy);
                _enteredInfinity |= double.IsInfinity(held);
            }

            return JsNumber.Create(held);
        }

        /// <summary>Writes a serialized value, putting back the original JSON of each marker and finding the losses.</summary>
        private void WriteMarked(
            JsonElement element,
            string path,
            string? arrayPath,
            Utf8JsonWriter writer,
            List<JavaScriptPrecisionLoss> losses,
            IReadOnlyDictionary<string, JsonElement> markers)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String when TryReadInfinity(element, out var infinity):
                    // JSON has no Infinity: it is written as null, and the number it came from carries that loss.
                    if (_lossyByValue.TryGetValue(infinity, out var lost))
                        losses.Add(new JavaScriptPrecisionLoss(path, lost, "null", arrayPath));
                    _wroteInfinity = true;
                    writer.WriteNullValue();
                    break;
                case JsonValueKind.String when markers.TryGetValue(element.GetString()!, out var origin):
                    origin.WriteTo(writer);
                    // Written from its original JSON: its lossy numbers keep every digit.
                    if (_markerPaths.TryGetValue(element.GetString()!, out var originPath))
                        WroteExactly(originPath);
                    break;
                case JsonValueKind.Number:
                    var text = element.GetRawText();
                    if (_lossyByValue.TryGetValue(JavaScriptNumberPrecision.Parse(text), out var lossy))
                        losses.Add(new JavaScriptPrecisionLoss(path, lossy, text, arrayPath));
                    element.WriteTo(writer);
                    break;
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        WriteMarked(property.Value, ValuePaths.Member(path, property.Name), arrayPath, writer, losses, markers);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                        WriteMarked(item, ValuePaths.Index(path, index++), arrayPath ?? path, writer, losses, markers);
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        /// <summary>
        /// Writes the stored object with the script's changes patched in: an unchanged member keeps its stored text, a
        /// changed object member is patched in turn, and anything else changed is written as the script holds it.
        /// </summary>
        private void Patch(
            JsonElement original,
            JsonElement current,
            string path,
            Utf8JsonWriter writer,
            List<string> changed,
            List<JavaScriptPrecisionLoss> losses,
            IReadOnlyDictionary<string, JsonElement> markers)
        {
            if (SameJavaScriptValue(original, current, markers))
            {
                // Kept as stored: its lossy numbers keep every digit, so they were not read and lost (FR-015).
                original.WriteTo(writer);
                WroteExactly(path);
                return;
            }

            if (original.ValueKind != JsonValueKind.Object || current.ValueKind != JsonValueKind.Object)
            {
                changed.Add(path);
                WriteMarked(current, path, arrayPath: null, writer, losses, markers);
                return;
            }

            var members = Members(current);
            var stored = Members(original);
            writer.WriteStartObject();
            foreach (var (name, value) in stored)
            {
                var memberPath = ValuePaths.Member(path, name);
                if (!members.TryGetValue(name, out var now))
                {
                    changed.Add(memberPath);
                    continue;
                }

                writer.WritePropertyName(name);
                Patch(value, now, memberPath, writer, changed, losses, markers);
            }

            foreach (var (name, value) in members)
            {
                if (stored.ContainsKey(name))
                    continue;
                var memberPath = ValuePaths.Member(path, name);
                changed.Add(memberPath);
                writer.WritePropertyName(name);
                WriteMarked(value, memberPath, arrayPath: null, writer, losses, markers);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Whether two JSON values are the same JavaScript value: numbers equal as doubles, object members equal in any
        /// order, arrays element by element. A marker in <paramref name="current"/> stands for the original it names.
        /// </summary>
        private bool SameJavaScriptValue(JsonElement original, JsonElement current, IReadOnlyDictionary<string, JsonElement>? markers)
        {
            if (markers is not null && current.ValueKind == JsonValueKind.String && markers.TryGetValue(current.GetString()!, out var origin))
                return SameJavaScriptValue(original, origin, null);

            switch (original.ValueKind)
            {
                case JsonValueKind.Number when TryReadInfinity(current, out var infinity):
                    return JavaScriptNumberPrecision.Parse(original.GetRawText()) == infinity;
                case JsonValueKind.Number:
                    return current.ValueKind == JsonValueKind.Number &&
                           JavaScriptNumberPrecision.Parse(original.GetRawText()) == JavaScriptNumberPrecision.Parse(current.GetRawText());
                case JsonValueKind.String:
                    return current.ValueKind == JsonValueKind.String && StringComparer.Ordinal.Equals(original.GetString(), current.GetString());
                case JsonValueKind.Object:
                {
                    if (current.ValueKind != JsonValueKind.Object)
                        return false;
                    var stored = Members(original);
                    var now = Members(current);
                    return stored.Count == now.Count &&
                           stored.All(member => now.TryGetValue(member.Key, out var value) && SameJavaScriptValue(member.Value, value, markers));
                }
                case JsonValueKind.Array:
                {
                    if (current.ValueKind != JsonValueKind.Array || original.GetArrayLength() != current.GetArrayLength())
                        return false;
                    using var left = original.EnumerateArray();
                    using var right = current.EnumerateArray();
                    while (left.MoveNext() && right.MoveNext())
                    {
                        if (!SameJavaScriptValue(left.Current, right.Current, markers))
                            return false;
                    }

                    return true;
                }
                default:
                    return original.ValueKind == current.ValueKind;
            }
        }

        /// <summary>Object members by name; a repeated name keeps its last value, as a JavaScript object does.</summary>
        private static Dictionary<string, JsonElement> Members(JsonElement element)
        {
            var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                members.Remove(property.Name);
                members[property.Name] = property.Value;
            }

            return members;
        }

        private static Utf8JsonWriter NewWriter(IBufferWriter<byte> buffer) =>
            new(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true });


        private sealed record Entry(JsonElement Element, JsValue Value);
    }
}

/// <summary>Paths of values inside an evaluation, such as <c>variables.order.items[0]</c> or <c>args['a b']</c>.</summary>
internal static class ValuePaths
{
    public static string Member(string path, string name) =>
        IsIdentifier(name) ? $"{path}.{name}" : $"{path}[{JsonSerializer.Serialize(name)}]";

    public static string Index(string path, int index) => $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";

    /// <summary>
    /// The root a path starts at: <c>variables.order</c> for <c>variables.order.items[0]</c>, <c>args</c> for
    /// <c>args.a</c>. Members below the root can be keys of a stored value, so a value-free diagnostic names roots only.
    /// </summary>
    public static string Root(string path)
    {
        var end = SegmentEnd(path, 0);
        if (!StringComparer.Ordinal.Equals(path[..end], "variables") || end >= path.Length)
            return path[..end];
        return path[..SegmentEnd(path, path[end] == '.' ? end + 1 : end)];
    }

    // The end of the segment starting at `start`: an identifier runs to the next '.' or '['; a bracket runs to its ']',
    // past a quoted name's escapes.
    private static int SegmentEnd(string path, int start)
    {
        if (start < path.Length && path[start] == '[')
        {
            var index = start + 1;
            var quoted = index < path.Length && path[index] == '"';
            for (index += quoted ? 1 : 0; index < path.Length; index++)
            {
                if (quoted && path[index] == '\\')
                    index++;
                else if (quoted && path[index] == '"')
                    quoted = false;
                else if (!quoted && path[index] == ']')
                    return index + 1;
            }

            return path.Length;
        }

        var next = path.IndexOfAny(['.', '['], start);
        return next < 0 ? path.Length : next;
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 &&
        (char.IsLetter(name[0]) || name[0] is '_' or '$') &&
        name.All(character => char.IsLetterOrDigit(character) || character is '_' or '$');
}

/// <summary>The bridge's cost per evaluation (spec 1228 FR-010), on the <c>Elsa.Expressions.JavaScript</c> meter.</summary>
internal static class JavaScriptValueBridgeMetrics
{
    public const string MeterName = "Elsa.Expressions.JavaScript";
    public const string EnterDuration = "elsa.javascript.value_bridge.enter.duration";
    public const string CollectDuration = "elsa.javascript.value_bridge.collect.duration";
    public const string BytesConverted = "elsa.javascript.value_bridge.bytes";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Enter = Meter.CreateHistogram<double>(EnterDuration, "ms", "Time the script value bridge spent converting values into one JavaScript evaluation.");
    private static readonly Histogram<double> Collect = Meter.CreateHistogram<double>(CollectDuration, "ms", "Time the script value bridge spent finding changes and converting values out of one JavaScript evaluation.");
    private static readonly Histogram<long> Bytes = Meter.CreateHistogram<long>(BytesConverted, "By", "JSON bytes the script value bridge converted in and out of one JavaScript evaluation.");

    public static void Record(TimeSpan enter, TimeSpan collect, long bytes)
    {
        Enter.Record(enter.TotalMilliseconds);
        Collect.Record(collect.TotalMilliseconds);
        Bytes.Record(bytes);
    }
}
