using System.Text;

namespace Elsa.Http.Core;

/// <summary>Canonical route shape shared by manifests and protected endpoint ownership; stimulus wire hashes stay unchanged.</summary>
public static class RouteTemplateIdentity
{
    /// <summary>Erases parameter names for collision checks while preserving constraints, defaults and catch-all shape.</summary>
    public static string Canonicalize(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var trimmed = route.Trim().Trim('/');
        var builder = new StringBuilder(trimmed.Length);
        var insideParameter = false;
        var parameterName = true;

        foreach (var character in trimmed)
        {
            if (character == '{')
            {
                insideParameter = true;
                parameterName = true;
                builder.Append(character);
                continue;
            }

            if (character == '}')
            {
                insideParameter = false;
                builder.Append(character);
                continue;
            }

            if (insideParameter && parameterName)
            {
                if (character == '*')
                {
                    builder.Append(character);
                    continue;
                }

                if (character is ':' or '=' or '?')
                {
                    parameterName = false;
                    builder.Append(character);
                }
                // Parameter names are intentionally erased. This makes {id} and {name} equivalent while keeping
                // catch-all/optional/constraint/default syntax in the canonical route.
                continue;
            }

            builder.Append(insideParameter ? character : char.ToLowerInvariant(character));
        }

        return "/" + builder;
    }
}
