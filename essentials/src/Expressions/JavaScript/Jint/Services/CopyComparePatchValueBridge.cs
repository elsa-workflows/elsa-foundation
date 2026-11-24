using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.Core.Models;
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
        => Begin(engine, undefinedAsNull: false);

    public IScriptValueEvaluation Begin(Engine engine, bool undefinedAsNull)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new Evaluation(engine, options.Value, undefinedAsNull);
    }

    private sealed class Evaluation : IScriptValueEvaluation
    {
        private const char MarkerStart = '';
        private readonly Engine _engine;
        private readonly FeatureOptions _options;
        private readonly bool _undefinedAsNull;
        private readonly List<ExecutionWarning> _warnings = [];
        public IReadOnlyList<ExecutionWarning> Warnings => _warnings;
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
        private long _enterTicks;
        private long _collectTicks;
        private long _bytes;
        private bool _disposed;

        public Evaluation(Engine engine, FeatureOptions options, bool undefinedAsNull)
        {
            _engine = engine;
            _options = options;
            _undefinedAsNull = undefinedAsNull;
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
                // Plain .NET over JSON: no script code runs here, so its memory is not the script's.
                JsonElement patched;
                try
                {
                    using (EvaluationLimits.ExcludeHostWork(_engine))
                    {
                        using (var writer = NewWriter(buffer))
                        {
                            Patch(entry.Element, marked.Root, key, writer, changed, losses, marked.Markers);
                        }

                        patched = JintResultMaterializer.ParseResult(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan), _options);
                    }
                }
                finally
                {
                    Forget(marked.Markers);
                }

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
            // Restored, not cleared: a toJSON met while previewing can call a host function that previews again.
            var outer = _previewing;
            _previewing = true;
            try
            {
                return Collect(key, current, assigned);
            }
            finally
            {
                _previewing = outer;
            }
        }

        private bool _previewing;
        private int _markerCount;

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
            if (IsUndefinedMarker(marked.Root))
            {
                Forget(marked.Markers);
                return new ScriptValueWrite(null, []);
            }
            var losses = new List<JavaScriptPrecisionLoss>();
            var buffer = new ArrayBufferWriter<byte>();
            // Plain .NET over JSON: no script code runs here, so its memory is not the script's.
            JsonElement json;
            try
            {
                using (EvaluationLimits.ExcludeHostWork(_engine))
                {
                    using (var writer = NewWriter(buffer))
                    {
                        WriteMarked(marked.Root, path, arrayPath: null, writer, losses, marked.Markers);
                    }

                    // The serialized value is returned as is only when nothing in it was replaced: no marker, no loss and
                    // no Infinity, which is written as null even when no lossy entered number has its sign.
                    json = JintResultMaterializer.ParseResult(System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan), _options);
                }
            }
            finally
            {
                Forget(marked.Markers);
            }

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
            // Restored, not cleared: a toJSON can call a host function whose variable read previews another value.
            var outer = _currentMarkers;
            _currentMarkers = markers;
            try
            {
                var text = Serialize(value, infinity: true, markers)
                    ?? throw new InvalidOperationException("A JavaScript result must be representable as persistable JSON.");
                using (EvaluationLimits.ExcludeHostWork(_engine))
                {
                    using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = _options.MaxResultDepth + 1 });
                    return (document.RootElement.Clone(), markers);
                }
            }
            catch
            {
                Forget(markers);
                throw;
            }
            finally
            {
                _currentMarkers = outer;
            }
        }

        /// <summary>
        /// <c>JSON.stringify</c> of a script value, marking infinite numbers and, given <paramref name="markers"/>, each
        /// unchanged entered object. Plain data is serialized in .NET (<see cref="PlainJson"/>), which runs no script code:
        /// host work, not counted against the script. Anything else goes through the engine's <c>JSON.stringify</c>, which
        /// can run the script's toJSON, getters or Proxy traps: counted, even inside host work.
        /// </summary>
        private string? Serialize(JsValue value, bool infinity, Dictionary<string, JsonElement>? markers)
        {
            // Inside a .NET serialization, comparing a nested value that is not plain would run the script's code in the
            // middle of it: instead the whole serialization gives up before any script code runs, and the engine does it
            // all, in its own order.
            var outerPlainOnly = _plainOnly;
            bool serialized;
            string? plain;
            try
            {
                _plainOnly = true;
                using (EvaluationLimits.ExcludeHostWork(_engine))
                    serialized = PlainJson.TryStringify(_engine, value, infinity ? InfinityMarkerText : null, markers is null ? null : MarkerFor, out plain,
                        infinity ? () => UndefinedMarkerText : null);
            }
            catch (NotPlainInside) when (!outerPlainOnly)
            {
                serialized = false;
                plain = null;
            }
            finally
            {
                _plainOnly = outerPlainOnly;
            }

            if (serialized)
                return plain;
            if (outerPlainOnly)
                throw NotPlainInside.Instance;

            // Markers taken before the serializer gave up are not in any text: start again.
            if (markers is not null)
            {
                Forget(markers);
                markers.Clear();
            }

            using (EvaluationLimits.CountScriptWork(_engine))
            {
                if (markers is not null)
                    return JintResultMaterializer.Stringify(_engine, value, _replacer ??= NewReplacer());
                return infinity
                    ? JintResultMaterializer.Stringify(_engine, value, InfinityOnlyReplacer())
                    : JintResultMaterializer.Stringify(_engine, value);
            }
        }

        // Whether a .NET serialization is running, which must not run the script's code.
        private bool _plainOnly;

        // Thrown up to the outermost .NET serialization when a value inside it is not plain.
        private sealed class NotPlainInside : Exception
        {
            public static readonly NotPlainInside Instance = new();
        }

        // A Mark's markers, once written: their origin paths are needed no longer, and every preview makes new ones.
        private void Forget(Dictionary<string, JsonElement> markers)
        {
            foreach (var marker in markers.Keys)
                _markerPaths.Remove(marker);
        }

        // The marker standing for an unchanged entered object, or null to serialize it.
        private string? MarkerFor(ObjectInstance instance)
        {
            if (_currentMarkers is null || !_origins.TryGetValue(instance, out var origin) || !IsUnchanged(instance))
                return null;
            // Numbered across the evaluation, not per Mark: a nested Mark (a toJSON calling a host function that reads
            // a variable) must not reuse a marker whose origin path the outer Mark still needs.
            var marker = _markerPrefix + (_markerCount++).ToString(CultureInfo.InvariantCulture);
            _currentMarkers[marker] = origin;
            _markerPaths[marker] = _originPaths[instance];
            return marker;
        }

        private Dictionary<string, JsonElement>? _currentMarkers;

        private JsValue NewReplacer()
        {
            var lookup = new ClrFunction(_engine, "lookup", (_, arguments) =>
            {
                var candidate = arguments.At(0);
                return candidate is ObjectInstance instance && MarkerFor(instance) is { } marker ? new JsString(marker) : candidate;
            });
            var infinity = InfinityMarker();
            // .NET, not JavaScript: nothing the script changed on the prototypes can intercept it, and it runs no script
            // statement, so the bridge's work stays apart from the script's in the evaluation limits.
            return new ClrFunction(_engine, "replacer", (holder, arguments) =>
            {
                if (holder is ObjectInstance array && array.IsArray() &&
                    array.GetOwnProperty(arguments.At(0)) == PropertyDescriptor.Undefined && arguments.At(0).ToString() != "")
                    return new JsString(UndefinedMarkerText);
                var value = UnboxNumber(arguments.At(1));
                if (value.IsUndefined())
                    return new JsString(UndefinedMarkerText);
                if (value.IsNumber() && !double.IsFinite(value.AsNumber()))
                    return infinity.Call(JsValue.Undefined, [value]);
                return value is ObjectInstance ? lookup.Call(JsValue.Undefined, [value]) : value;
            });
        }

        /// <summary>
        /// JSON writes <c>Infinity</c> as <c>null</c>. A number that entered as a value too large for a double holds
        /// <c>Infinity</c>, so on the way out it is marked: an unchanged one still compares equal to what entered, and one
        /// that is written is a loss (spec 1228 FR-011: a number that becomes Infinity is lossy).
        /// </summary>
        private ClrFunction InfinityMarker() =>
            new(_engine, "infinity", (_, arguments) => new JsString(InfinityMarkerText(arguments.At(0).AsNumber())));

        private string UndefinedMarkerText => _markerPrefix + "undefined";
        private bool IsUndefinedMarker(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() == UndefinedMarkerText;
        private string InfinityMarkerText(double value) => _markerPrefix + (double.IsNaN(value) ? "nan" : value > 0 ? "+inf" : "-inf");

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
            else if (StringComparer.Ordinal.Equals(text, InfinityMarkerText(double.NaN)))
                value = double.NaN;
            else
                return false;
            return true;
        }

        private JsValue InfinityOnlyReplacer()
        {
            if (_infinityReplacer is not null)
                return _infinityReplacer;
            var infinity = InfinityMarker();
            return _infinityReplacer = new ClrFunction(_engine, "replacer", (holder, arguments) =>
            {
                if (holder is ObjectInstance array && array.IsArray() &&
                    array.GetOwnProperty(arguments.At(0)) == PropertyDescriptor.Undefined && arguments.At(0).ToString() != "")
                    return new JsString(UndefinedMarkerText);
                var value = UnboxNumber(arguments.At(1));
                if (value.IsUndefined())
                    return new JsString(UndefinedMarkerText);
                return value.IsNumber() && !double.IsFinite(value.AsNumber()) ? infinity.Call(JsValue.Undefined, [value]) : value;
            });
        }

        private static JsValue UnboxNumber(JsValue value) =>
            value is IJsPrimitive { Type: Types.Number } ? JsNumber.Create(TypeConverter.ToNumber(value)) : value;

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

            var text = Serialize(instance, infinity: true, markers: null);
            bool unchanged;
            if (text is null)
                unchanged = false;
            else
            {
                using var hostWork = EvaluationLimits.ExcludeHostWork(_engine);
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
            IReadOnlyDictionary<string, JsonElement> markers,
            bool arrayElement = false)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String when IsUndefinedMarker(element):
                    if (arrayElement)
                        Warn("VF-UNDEF-003", path, "An undefined array element or hole became a null placeholder.");
                    writer.WriteNullValue();
                    break;
                case JsonValueKind.String when TryReadInfinity(element, out var infinity):
                    // JSON has no Infinity: it is written as null, and the number it came from carries that loss.
                    if (_lossyByValue.TryGetValue(infinity, out var lost))
                        losses.Add(new JavaScriptPrecisionLoss(path, lost, "null", arrayPath));
                    else
                        Warn("VF-NUM-001", path, "A computed non-finite number became null.");
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
                        if (IsUndefinedMarker(property.Value) && !_undefinedAsNull)
                        {
                            Warn("VF-UNDEF-002", ValuePaths.Member(path, property.Name), "An undefined object property was dropped.");
                            continue;
                        }
                        writer.WritePropertyName(property.Name);
                        WriteMarked(property.Value, ValuePaths.Member(path, property.Name), arrayPath, writer, losses, markers);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                        WriteMarked(item, ValuePaths.Index(path, index++), arrayPath ?? path, writer, losses, markers, arrayElement: true);
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private void Warn(string code, string path, string message)
        {
            if (_previewing)
                return;
            var root = ValuePaths.Root(path);
            _warnings.Add(new ExecutionWarning(code, $"{message} Path: {path}.", ExecutionWarningSeverity.Warning)
            {
                ProtectedForm = new ExecutionWarning(code, $"{message} Path: {root}.", ExecutionWarningSeverity.Warning)
            });
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

                if (IsUndefinedMarker(now) && !_undefinedAsNull)
                {
                    changed.Add(memberPath);
                    Warn("VF-UNDEF-002", memberPath, "An undefined object property was dropped.");
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
                if (IsUndefinedMarker(value) && !_undefinedAsNull)
                {
                    Warn("VF-UNDEF-002", memberPath, "An undefined object property was dropped.");
                    continue;
                }
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
