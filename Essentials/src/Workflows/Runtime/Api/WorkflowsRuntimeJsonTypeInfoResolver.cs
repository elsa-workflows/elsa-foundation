using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>
/// Supplies Runtime API source-generated metadata to ASP.NET Core using the effective
/// options instance. The generated context's resolver implementation calls its private
/// source factories with those exact options, avoiding the read-only cached Default path.
/// </summary>
internal sealed class WorkflowsRuntimeJsonTypeInfoResolver : IJsonTypeInfoResolver
{
    private readonly WorkflowsRuntimeJsonContext _context = new();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = ((IJsonTypeInfoResolver)_context).GetTypeInfo(type, options);
        return typeInfo;
    }
}
