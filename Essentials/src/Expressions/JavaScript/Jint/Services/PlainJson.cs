using System.Globalization;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// <c>JSON.stringify</c> in .NET for plain data, giving the same text as Jint's: plain objects and arrays with data
/// properties only, strings, numbers, booleans and null. <c>JSON.stringify</c> can run the script's code (a toJSON, own
/// or inherited, a getter, a Proxy trap), so it is counted against the script's limits; on plain data it runs none, and
/// this serializer proves that as it goes. Anything it cannot prove plain — a toJSON anywhere on the way, an accessor, a
/// Proxy, another kind of object, a function, a BigInt, an array hole, a cycle — makes it give up, and the caller falls
/// back to <c>JSON.stringify</c>. So it changes the cost of serializing, never its result.
/// </summary>
internal static class PlainJson
{
    private const int MaxDepth = 1_000;

    private static readonly JsString ToJsonKey = new("toJSON");
    private static readonly JsString LengthKey = new("length");

    /// <summary>
    /// Serializes <paramref name="value"/> as <c>JSON.stringify(value, replacer)</c> would, where the replacer turns an
    /// infinite number into <paramref name="infinity"/>'s text when given, and an object into
    /// <paramref name="marker"/>'s text when it returns one. Returns <see langword="false"/> when the value is not provably
    /// plain; <paramref name="text"/> is <see langword="null"/> when the value has no JSON form (undefined, a symbol).
    /// </summary>
    public static bool TryStringify(
        Engine engine,
        JsValue value,
        Func<double, string>? infinity,
        Func<ObjectInstance, string?>? marker,
        out string? text)
    {
        text = null;
        var intrinsics = engine.Intrinsics;
        var objectPrototype = intrinsics.Object.PrototypeObject;
        var arrayPrototype = intrinsics.Array.PrototypeObject;
        if (!PrototypesArePlain(objectPrototype, arrayPrototype))
            return false;

        var writer = new Writer(objectPrototype, arrayPrototype, infinity, marker);
        var builder = new StringBuilder();
        switch (writer.Write(value, builder, depth: 0))
        {
            case Outcome.Written:
                text = builder.ToString();
                return true;
            case Outcome.Undefined:
                return true;
            default:
                return false;
        }
    }

    // An inherited toJSON would run for every object or array: none on Object.prototype or Array.prototype, and
    // Array.prototype inheriting from Object.prototype only (its prototype can be changed; Object.prototype's cannot).
    private static bool PrototypesArePlain(ObjectInstance objectPrototype, ObjectInstance arrayPrototype) =>
        objectPrototype.GetOwnProperty(ToJsonKey) == PropertyDescriptor.Undefined &&
        arrayPrototype.GetOwnProperty(ToJsonKey) == PropertyDescriptor.Undefined &&
        arrayPrototype.Prototype == objectPrototype;

    private enum Outcome
    {
        Written,
        Undefined,
        NotPlain
    }

    private sealed class Writer(
        ObjectInstance objectPrototype,
        ObjectInstance arrayPrototype,
        Func<double, string>? infinity,
        Func<ObjectInstance, string?>? marker)
    {
        private readonly HashSet<ObjectInstance> _open = new(ReferenceEqualityComparer.Instance);

        public Outcome Write(JsValue value, StringBuilder json, int depth)
        {
            if (value.IsNull())
            {
                json.Append("null");
                return Outcome.Written;
            }

            if (value.IsUndefined() || value.IsSymbol())
                return Outcome.Undefined;
            if (value.IsBoolean())
            {
                json.Append(value.AsBoolean() ? "true" : "false");
                return Outcome.Written;
            }

            if (value.IsString())
            {
                Quote(value.ToString(), json);
                return Outcome.Written;
            }

            if (value.IsNumber())
            {
                var number = value.AsNumber();
                if (double.IsInfinity(number) && infinity is not null)
                    Quote(infinity(number), json);
                else
                    AppendNumber(number, json);
                return Outcome.Written;
            }

            if (value is not ObjectInstance instance || depth >= MaxDepth)
                return Outcome.NotPlain;
            var isArray = instance is JsArray;
            // Exactly a plain object or an array, with the intrinsic prototype and no toJSON of its own: no script code is
            // reached through its shape. A Proxy, a function, a boxed primitive, a Date or a class instance is not plain.
            if (isArray ? instance.Prototype != arrayPrototype : instance.GetType() != typeof(JsObject) || instance.Prototype != objectPrototype)
                return Outcome.NotPlain;
            if (instance.GetOwnProperty(ToJsonKey) != PropertyDescriptor.Undefined)
                return Outcome.NotPlain;
            if (marker is not null)
            {
                var replaced = marker(instance);
                // The marker can run the script's code (comparing a value that is not plain goes through the engine):
                // what was plain before it may be no longer.
                if (!PrototypesArePlain(objectPrototype, arrayPrototype))
                    return Outcome.NotPlain;
                if (replaced is not null)
                {
                    Quote(replaced, json);
                    return Outcome.Written;
                }
            }

            if (!_open.Add(instance))
                return Outcome.NotPlain; // a cycle: JSON.stringify throws, so let it
            try
            {
                return isArray ? WriteArray(instance, json, depth) : WriteObject(instance, json, depth);
            }
            finally
            {
                _open.Remove(instance);
            }
        }

        private Outcome WriteArray(ObjectInstance array, StringBuilder json, int depth)
        {
            var lengthDescriptor = array.GetOwnProperty(LengthKey);
            if (!lengthDescriptor.IsDataDescriptor())
                return Outcome.NotPlain;
            var length = TypeConverter.ToUint32(lengthDescriptor.Value);
            json.Append('[');
            for (uint index = 0; index < length; index++)
            {
                if (index > 0)
                    json.Append(',');
                // A hole reads through the prototype chain: not provably plain.
                var element = array.GetOwnProperty(JsNumber.Create(index));
                if (element == PropertyDescriptor.Undefined || !element.IsDataDescriptor())
                    return Outcome.NotPlain;
                switch (Write(element.Value, json, depth + 1))
                {
                    case Outcome.Undefined:
                        json.Append("null");
                        break;
                    case Outcome.NotPlain:
                        return Outcome.NotPlain;
                }
            }

            json.Append(']');
            return Outcome.Written;
        }

        private Outcome WriteObject(ObjectInstance instance, StringBuilder json, int depth)
        {
            json.Append('{');
            var any = false;
            foreach (var key in instance.GetOwnPropertyKeys(Types.String))
            {
                var property = instance.GetOwnProperty(key);
                if (property == PropertyDescriptor.Undefined || !property.Enumerable)
                    continue;
                if (!property.IsDataDescriptor())
                    return Outcome.NotPlain;
                var start = json.Length;
                if (any)
                    json.Append(',');
                Quote(key.ToString(), json);
                json.Append(':');
                switch (Write(property.Value, json, depth + 1))
                {
                    case Outcome.Undefined:
                        json.Length = start;
                        break;
                    case Outcome.NotPlain:
                        return Outcome.NotPlain;
                    default:
                        any = true;
                        break;
                }
            }

            json.Append('}');
            return Outcome.Written;
        }
    }

    // As Jint's SerializeJSONProperty: a whole number a long holds is written as that long, NaN and Infinity as null, and
    // any other number as JavaScript's Number::toString.
    private static void AppendNumber(double number, StringBuilder json)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            json.Append("null");
        else if (number > -9.223372036854776E+18 && number < 9.223372036854776E+18 && Math.Abs(number % 1.0) <= 4.94E-322)
            json.Append(((long)number).ToString(CultureInfo.InvariantCulture));
        else
            json.Append(TypeConverter.ToString(JsNumber.Create(number)));
    }

    // As Jint's QuoteJSONString: only quote, backslash, the short escapes, other control characters and lone surrogates
    // are escaped, as \u with four lowercase hex digits.
    private static void Quote(string value, StringBuilder json)
    {
        json.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            switch (c)
            {
                case '"':
                    json.Append("\\\"");
                    continue;
                case '\\':
                    json.Append("\\\\");
                    continue;
                case '\b':
                    json.Append("\\b");
                    continue;
                case '\f':
                    json.Append("\\f");
                    continue;
                case '\n':
                    json.Append("\\n");
                    continue;
                case '\r':
                    json.Append("\\r");
                    continue;
                case '\t':
                    json.Append("\\t");
                    continue;
            }

            if (char.IsSurrogatePair(value, index))
            {
                json.Append(c).Append(value[index + 1]);
                index++;
            }
            else if (c < ' ' || char.IsSurrogate(c))
                json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                json.Append(c);
        }

        json.Append('"');
    }
}
