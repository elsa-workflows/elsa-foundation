using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using System.Diagnostics.CodeAnalysis;

namespace Elsa.Expressions.JavaScript.Jint.Converters
{
    internal sealed class EnumToStringConverter : IObjectConverter
    {
        public bool TryConvert(Engine engine, object value, [NotNullWhen(true)] out JsValue? result)
        {
            if (value is Enum)
            {
                result = value.ToString();
                return true;
            }

            result = JsValue.Null;
            return false;
        }
    }
}
