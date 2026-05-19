using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint3.Converters
{
    /// <summary>
    /// Converts a byte array to a <see cref="JsValue"/> instance representing a Uint8Array.
    /// </summary>
    internal sealed class ByteArrayConverter : IObjectConverter
    {
        public bool TryConvert(Engine engine, object value, [NotNullWhen(true)] out JsValue? result)
        {
            if (value is byte[] bytes)
            {
                result = engine.Intrinsics.ArrayBuffer.Construct(bytes);
                return true;
            }

            result = JsValue.Null;
            return false;
        }
    }
}
