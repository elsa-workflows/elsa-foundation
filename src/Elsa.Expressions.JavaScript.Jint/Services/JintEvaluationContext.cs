using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Extensions;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using System.Collections;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    public sealed class JintEvaluationContext 
        : IJavaScriptEvaluationContext
    {
        internal readonly Dictionary<string, object?> Values = [];
        internal readonly List<IJavaScriptFunction> Functions = [];
        internal readonly List<Type> Types = [];
        internal readonly List<string> Libraries = [];

        public object? GetValue(string name)
        {
            var result = Values.TryGetValue(name, out var value)
                ? value 
                : null;

            if (result is JsObject jsObject)
                return jsObject.ToObject();

            return result;
        }
  
        public void AddType(Type type)=> Types.Add(type);

        public void AddFunction(IJavaScriptFunction function) => Functions.Add(function);

        public void AddResource(Stream stream)
        {
            try
            {
                using var reader = new StreamReader(stream);
                var script = reader.ReadToEnd();
                Libraries.Add(script);
            }
            finally
            {
                stream.Dispose();
            }
        }
       
        public TValue? GetValue<TValue>(string name)
        {
            return (TValue?)GetValue(name);
        }

        public void SetValue(string name, object? value) => Values[name] = value;
    }
}
