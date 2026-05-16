using Elsa.FastEndpoints.Contracts;
using Elsa.Primitives.Extensions;
using FastEndpoints;

namespace Elsa.FastEndpoints.Filters
{
    public sealed class FastEndpointsFilter : IFastEndpointFilter
    {
        public string ExcludedNamespace { get; set; } = string.Empty;

        public IEnumerable<string> ExcludedMethods { get; set; } = [];

        public IEnumerable<string> IncludedMethods { get; set; } = [];

        public IEnumerable<string> IncludedTypes { get; set; } = [];

        public IEnumerable<string> ExcludedTypes { get; set; } = [];

        void Validate()
        {
            if(string.IsNullOrWhiteSpace(ExcludedNamespace))
            {
                throw new InvalidOperationException("ExcludedNamespace cannot be null or whitespace.");
            }
            if(ExcludedMethods.Any(IncludedMethods.Contains))
            {
                throw new InvalidOperationException($"Excluded and included methods contain a duplicate method. Please remove the duplicate method(s)");
            }

            if (ExcludedTypes.Any(IncludedTypes.Contains))
            {
                throw new InvalidOperationException($"Excluded and included types contain a duplicate type. Please remove the duplicate type(s)");
            }
        }

        public bool Exclude(EndpointDefinition endpointDefinition)
        {
            Validate();

            var excludedNamespace = endpointDefinition.EndpointType.Namespace != ExcludedNamespace;
            if (!excludedNamespace)
            {
                return false;
            }

            foreach(var method in IncludedMethods)
            {
                if(endpointDefinition.Verbs.Contains(method, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            foreach (var typeName in IncludedTypes)
            {
                var type = typeName.GetLoadedType();
                if (endpointDefinition.EndpointType == type)
                {
                    return false;
                }
            }

            foreach (var method in ExcludedMethods)
            {
                if (endpointDefinition.Verbs.Contains(method, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }


            foreach (var typeName in ExcludedTypes)
            {
                var type = typeName.GetLoadedType();
                if (endpointDefinition.EndpointType == type)
                {
                    return true;
                }
            }

            return true;
        }
    }
}
