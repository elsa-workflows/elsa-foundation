using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Primitives.Extensions;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    public sealed class JintValueNormalizer : IJintValueNormalizer
    {
        public object? Normalize(Engine engine, object? value)
        {
            if (value == null)
                return null;

            if (value is ExpandoObject expandoObject)
                return ConvertToJsObject(engine, expandoObject);

            if (value is IDictionary<string, object?> dictionary)
                ConvertToJsObject(engine, dictionary);

            return value;
        }

        private ObjectInstance ConvertToJsObject(Engine engine, IDictionary<string, object?> expando)
        {
            var jsObject = engine.Intrinsics.Object.Construct([]);

            foreach (var kvp in expando)
            {
                var value = kvp.Value;
                var jsValue = ConvertToJsValue(engine, value);
                var propertyDescriptor = new PropertyDescriptor(jsValue, true, true, true);
                jsObject.DefineOwnProperty(kvp.Key, propertyDescriptor);
            }

            return jsObject;
        }

        private JsValue ConvertToJsValue(Engine engine, object? value)
        {
            if (value == null)
                return JsValue.Null;

            if (value is IDictionary<string, object?> dict)
                return ConvertToJsObject(engine, dict);

            var valueType = value.GetType();
            if (valueType.IsCollectionType())
            {
                var list = (ICollection)value;
                var jsArray = engine.Intrinsics.Array.Construct(list.Count);
                var index = 0;

                foreach (var item in list)
                    jsArray.Set(index++, ConvertToJsValue(engine, item), true);

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
}
