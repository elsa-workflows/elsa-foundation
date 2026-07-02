using Elsa.Expressions.JavaScript.Core.Models;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Constants;

internal static class DefaultTypeDescriptors
{
    internal static IEnumerable<JavaScriptTypeDescriptor> Get()
    {
        // Common .NET types.
        var result = new List<Type>()
        {
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid),
            typeof(Random),
            typeof(object),
            typeof(ExpandoObject),
            typeof(string),
            typeof(bool),
            typeof(short),

            typeof(int),
            typeof(long),
            typeof(decimal),
            typeof(float),
            typeof(double),
            typeof(byte[]),
            typeof(Stream),
            typeof(DateOnly),
            typeof(TimeOnly),
            typeof(IDictionary<string, object>)
        };

        return result.Select(x => new JavaScriptTypeDescriptor(x));
    }
}