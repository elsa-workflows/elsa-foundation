using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Default <see cref="IWorkflowArtifactClosureSerializer"/>: encodes the closure envelope through
/// <see cref="IPayloadSerializer"/>'s options, extended with the durable store's projection-drop discipline.
/// </summary>
/// <remarks>
/// <para>
/// The added modifier is deliberately the same rule
/// <c>GroundworkRuntimeDocumentSerializer.ConfigureDurableDocumentShape</c> applies: drop
/// <see cref="WorkflowExecutable.Nodes"/> and <see cref="WorkflowExecutable.NodesById"/>, which the constructor
/// rebuilds by flattening <see cref="WorkflowExecutable.RootActivity"/>. Persisting or shipping them would
/// duplicate the whole graph and would make an exported artifact and a store-round-tripped one differ in bytes
/// for no semantic reason.
/// </para>
/// <para>
/// <b>What is deliberately NOT copied from the Groundwork serializer.</b> That serializer also <em>adds</em>
/// synthetic stimulus lookup-key properties to <c>BookmarkState</c> and <c>WorkflowTriggerBinding</c>. Those are
/// index fields the document providers query on — storage machinery, not artifact content — and an envelope that
/// carried them would be asserting one engine's index layout on another's. The envelope's carried bindings are
/// provenance the importer recomputes anyway (D4).
/// </para>
/// <para>
/// Options are derived per <see cref="IPayloadSerializer.GetOptions"/> instance rather than per call. That
/// serializer rebuilds and caches its options only when the converter registry's revision changes, so caching on
/// reference identity of the returned instance tracks its invalidation exactly, and System.Text.Json's reflected
/// metadata cache survives between calls.
/// </para>
/// </remarks>
public sealed class WorkflowArtifactClosureSerializer(IPayloadSerializer payloadSerializer) : IWorkflowArtifactClosureSerializer
{
    private readonly object _optionsLock = new();
    private JsonSerializerOptions? _cachedOptions;
    private JsonSerializerOptions? _cachedSource;

    public string Serialize(WorkflowArtifactClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);
        return JsonSerializer.Serialize(closure, GetOptions());
    }

    public WorkflowArtifactClosure? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<WorkflowArtifactClosure>(json, GetOptions());
    }

    private JsonSerializerOptions GetOptions()
    {
        var source = payloadSerializer.GetOptions();

        var cached = Volatile.Read(ref _cachedOptions);
        if (cached is not null && ReferenceEquals(Volatile.Read(ref _cachedSource), source))
            return cached;

        lock (_optionsLock)
        {
            if (_cachedOptions is not null && ReferenceEquals(_cachedSource, source))
                return _cachedOptions;

            var options = Derive(source);
            _cachedSource = source;
            Volatile.Write(ref _cachedOptions, options);
            return options;
        }
    }

    // Copy-construct so the payload serializer's own (already-frozen) options are never mutated, then layer the
    // projection drop on top of whatever resolver it built. WithAddedModifier runs last, so removal happens after
    // the deterministic member sort and the remaining order is untouched.
    private static JsonSerializerOptions Derive(JsonSerializerOptions source) =>
        new(source)
        {
            TypeInfoResolver = (source.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                .WithAddedModifier(DropRecomputedProjections)
        };

    private static void DropRecomputedProjections(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(WorkflowExecutable))
            RemoveProperties(typeInfo, nameof(WorkflowExecutable.Nodes), nameof(WorkflowExecutable.NodesById));
    }

    private static void RemoveProperties(JsonTypeInfo typeInfo, params string[] propertyNames)
    {
        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var memberName = (typeInfo.Properties[i].AttributeProvider as MemberInfo)?.Name;
            if (memberName is not null && propertyNames.Contains(memberName, StringComparer.Ordinal))
                typeInfo.Properties.RemoveAt(i);
        }
    }
}
