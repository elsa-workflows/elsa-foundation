using Elsa.Api.FastEndpoints.Contracts;
using Elsa.Primitives.Extensions;
using FastEndpoints;

namespace Elsa.Api.FastEndpoints.Filters;

public sealed class FastEndpointsFilter : IFastEndpointFilter
{
    public FastEndpointsFilter()
    {
    }

    public string Namespace { get; set; } = string.Empty;

    public IEnumerable<string> WhitelistedMethods { get; set; } = [];


    public IEnumerable<string> WhitelistedTypes { get; set; } = [];


    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Namespace))
        {
            throw new InvalidOperationException("Namespace cannot be null or whitespace.");
        }
    }

    public bool Exclude(EndpointDefinition endpointDefinition)
    {
        Validate();

        var existsInNamespace = ExistsInNamespace(endpointDefinition.EndpointType.Namespace);
        if (!existsInNamespace)
        {
            return false;
        }

        foreach (var method in WhitelistedMethods)
        {
            if (endpointDefinition.Verbs.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var typeName in WhitelistedTypes)
        {
            var type = typeName.GetLoadedType();
            if (endpointDefinition.EndpointType == type)
            {
                return false;
            }
        }

        return true;
    }

    private bool ExistsInNamespace(string? endpointNamespace)
    {
        if (string.IsNullOrWhiteSpace(endpointNamespace))
        {
            return false;
        }

        if (Namespace.EndsWith('*'))
        {
            var trimmedNamespace = Namespace.TrimEnd('*');
            return endpointNamespace.StartsWith(trimmedNamespace);
        }

        return endpointNamespace.Equals(Namespace, StringComparison.OrdinalIgnoreCase);
    }
}