using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Providers
{
    internal sealed class CommonTypeDescriptorsProvider : IJavaScriptTypeDescriptorProvider
    {
        public IEnumerable<JavaScriptTypeDescriptor> GetDescriptors()
        {
            // Common .NET types.
            var result = new (Type type, string? alias)[]
            {
                (typeof(DateTime), WellKnownTypeNames.Date),
                (typeof(DateTimeOffset),WellKnownTypeNames.Date),
                (typeof(TimeSpan),null),
                (typeof(Guid), null),
                (typeof(Random), null),
                (typeof(object), WellKnownTypeNames.Any),
                (typeof(ExpandoObject), WellKnownTypeNames.Any),
                (typeof(string), WellKnownTypeNames.String),
                (typeof(bool), WellKnownTypeNames.Bool),
                (typeof(short), WellKnownTypeNames.Number),
                (typeof(int), WellKnownTypeNames.Number),
                (typeof(long), WellKnownTypeNames.Number),
                (typeof(decimal), WellKnownTypeNames.Decimal),
                (typeof(float), WellKnownTypeNames.Single),
                (typeof(double), WellKnownTypeNames.Double),
                (typeof(byte[]), WellKnownTypeNames.Buffer),
                (typeof(Stream), WellKnownTypeNames.Stream),
                (typeof(Guid), WellKnownTypeNames.Guid),
                (typeof(DateOnly), WellKnownTypeNames.Date),
                (typeof(TimeOnly), WellKnownTypeNames.Date),
                (typeof(IDictionary<string, object>), WellKnownTypeNames.ObjectDictionary)
            };

            return result.Select(x =>
            {
                var descriptor = new JavaScriptTypeDescriptor(x.type);

                if (!string.IsNullOrWhiteSpace(x.alias))
                    descriptor.Alias = x.alias;

                return descriptor;
            });
        }
    }
}
