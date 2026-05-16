using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Primitives.Extensions
{
    public static class StringArrayExtensions
    {
        public static string JoinSegments(this IEnumerable<string?> segments, string separator = "/")
        {
            return string.Join(separator, segments.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim('/')));
        }
    }
}
