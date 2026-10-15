using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Models;

namespace Elsa3.Mapping.ActivityMap.Rewrites;

/// <summary>
/// A named node-rewrite strategy (framework §2.24.2 Strategy): conversion logic a map entry cannot express as data.
/// The normalizer runs the entry's strategy after the member rules, before descending into children.
/// </summary>
internal interface IElsa3NodeRewrite
{
    Elsa3NodeRewriteKind Kind { get; }

    ValueTask ApplyAsync(Elsa3RewriteContext context, CancellationToken cancellationToken);
}

/// <summary>What a rewrite strategy sees and may change for one node.</summary>
internal sealed class Elsa3RewriteContext(
    Elsa3ConversionPlan plan,
    Elsa3NodePlan node,
    Elsa3ActivityResolver resolver,
    Func<string, TypeReference> resolveType,
    Func<JsonElement, string, Elsa3Activity?> readChild)
{
    public Elsa3ConversionPlan Plan { get; } = plan;

    public Elsa3NodePlan Node { get; } = node;

    public Elsa3Activity Activity => Node.Node;

    public Elsa3ActivityResolver Resolver { get; } = resolver;

    public bool IsReusable => Plan.Request.IsReusable;

    /// <summary>Maps an Elsa 3 type token to an Elsa 4 type reference.</summary>
    public TypeReference ResolveType(string elsa3TypeToken) => resolveType(elsa3TypeToken);

    /// <summary>Reads a port child from its JSON, records its original path, and appends it to the node's children.</summary>
    public Elsa3Activity? AddChild(string slotName, string? label, JsonElement activityJson, string jsonPath)
    {
        var child = readChild(activityJson, jsonPath);
        Node.Slots.Add(new Elsa3SlotChild(slotName, label, child));
        if (child is not null)
        {
            Activity.Activities ??= [];
            Activity.Activities.Add(child);
        }
        return child;
    }

    /// <summary>The property's value, matched case-insensitively, when present.</summary>
    public bool TryGetProperty(string elsa3Name, out string propertyName, out JsonElement value)
    {
        foreach (var (name, element) in Activity.AdditionalProperties)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(name, elsa3Name))
            {
                propertyName = name;
                value = element;
                return true;
            }
        }

        propertyName = Elsa3ConversionDiagnostics.JsonName(elsa3Name);
        value = default;
        return false;
    }

    public void RemoveProperty(string elsa3Name)
    {
        if (TryGetProperty(elsa3Name, out var propertyName, out _))
            Activity.AdditionalProperties.Remove(propertyName);
    }

    public void SetProperty(string key, JsonElement value) => Activity.AdditionalProperties[key] = value;

    public void Error(string code, string message, string guidance, string? property = null)
    {
        Node.Failed = true;
        Plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Error(Plan, Node, code, message, guidance, property));
    }

    public void Warning(string code, string message, string guidance, string? property = null) =>
        Plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Warning(Plan, Node, code, message, guidance, property));

    /// <summary>The node's ancestors, nearest first.</summary>
    public IEnumerable<Elsa3NodePlan> Ancestors()
    {
        var parent = Node.Parent;
        while (parent is not null && Plan.Find(parent) is { } parentPlan)
        {
            yield return parentPlan;
            parent = parentPlan.Parent;
        }
    }
}
