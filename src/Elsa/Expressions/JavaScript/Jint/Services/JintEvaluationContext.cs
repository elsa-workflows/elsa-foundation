using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Primitives.Extensions;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using System.Collections;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Jint.Services;

public sealed class JintExecutionContext(IPreparedScriptFactory preparedScriptFactory, Engine engine) : IJavaScriptExecutionContext
{
    public object? Evaluate(string script)
    {
        var preparedScript = preparedScriptFactory.Create(script);
        var result = engine.Evaluate(preparedScript);
        return result.UnwrapIfPromise().ToObject();
    }

    public void Execute(string script)
    {
        var preparedScript = preparedScriptFactory.Create(script);
        _ = engine.Execute(preparedScript);
    }

    public object? GetValue(string name)
    {
        var result = engine.GetValue(name).ToObject();

        if (result is JsObject jsObject)
            return jsObject.ToObject();

        return result;
    }

    public void SetValue(string name, object? value)
    {
        engine.SetValue(
            name,
            value
        );
    }

    public object? NormalizeValue(object? value)
    {
        if (value == null)
            return null;

        if (value is not ExpandoObject expandoObject)
            return value;

        return ConvertToJsObject(expandoObject);
    }

    public void RegisterFunction(IJavaScriptFunction function)
    {
        engine.SetValue(
            function.Name,
            function.Delegate
        );
    }

    public void RegisterType(Type type)
        => engine.SetValue(type.Name, TypeReference.CreateTypeReference(engine, type));

    private ObjectInstance ConvertToJsObject(IDictionary<string, object?> expando)
    {
        var jsObject = engine.Intrinsics.Object.Construct([]);

        foreach (var kvp in expando)
        {
            var value = kvp.Value;
            var jsValue = ConvertToJsValue(value);
            var propertyDescriptor = new PropertyDescriptor(jsValue, true, true, true);
            jsObject.DefineOwnProperty(kvp.Key, propertyDescriptor);
        }

        return jsObject;
    }

    private JsValue ConvertToJsValue(object? value)
    {
        if (value == null)
            return JsValue.Null;

        if (value is IDictionary<string, object?> dict)
            return ConvertToJsObject(dict);

        var valueType = value.GetType();
        if (valueType.IsCollectionType())
        {
            // IsCollectionType() admits types that only implement ICollection<T> (e.g. HashSet<T>),
            // which do not implement the non-generic ICollection — so enumerate generically (#407).
            var jsArray = engine.Intrinsics.Array.Construct(0);
            var index = 0;

            foreach (var item in (IEnumerable)value)
                jsArray.Set(index++, ConvertToJsValue(item), true);

            return jsArray;
        }

        if (value is string str)
            return JsValue.FromObject(engine, str);

        if (value is int or double or float or decimal)
            return JsValue.FromObject(engine, Convert.ToDouble(value));

        if (value is bool b)
            return JsValue.FromObject(engine, b);

        return JsValue.FromObject(engine, value);
    }
}