using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Primitives.Extensions
{
    public static class StringExtensions
    {
        public static string Camelize(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            if (value.Length == 1)
                return value.ToLower();

            return char.ToLower(value[0]) + value.Substring(1);
        }
    }
}
