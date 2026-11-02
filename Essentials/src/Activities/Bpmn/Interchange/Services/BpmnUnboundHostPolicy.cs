using System.Xml.Linq;
using LibInterchange = global::Bpmn.Interchange;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// The one host-side rule the library cannot state for us: <b>a boundary event needs a bound Elsa child to
/// attach to</b>.
/// <para>
/// The library models every task as binding work — a <c>userTask</c> it cannot implement still gets an
/// <see cref="LibInterchange.BpmnWorkBinding.UnboundTask"/> and therefore a binding ref — so its "host binds no
/// work" guard never fires for a task. Elsa's rule is narrower: a task imported from XML carries no child
/// activity until an author binds one, and the graph validator rejects a boundary on a childless host. Rather
/// than let the importer emit a graph the validator would reject, the offending boundaries are identified here,
/// straight from the source document, and dropped from the read with Elsa's own finding.
/// </para>
/// <para>
/// This runs before the read (not after) because the library drops some of these itself, for its own reasons
/// and with its own wording; identifying them up front lets the adapter suppress those findings by element id
/// rather than by matching message text.
/// </para>
/// </summary>
internal static class BpmnUnboundHostPolicy
{
    private static readonly XNamespace Model = LibInterchange.BpmnXmlNames.Model;

    /// <summary>Element types a boundary event may attach to at all; anything else is the library's finding to report.</summary>
    private static readonly IReadOnlySet<string> BoundaryHostLocalNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "subProcess", "transaction", "callActivity", "task", "userTask", "serviceTask", "scriptTask",
        "manualTask", "businessRuleTask", "sendTask", "receiveTask"
    };

    /// <summary>
    /// The boundary events Elsa must drop, mapped to the finding that says why. Scans every process and every
    /// nested container, because a boundary resolves its host within its own container.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ScanBoundariesOnUnboundHosts(XDocument document)
    {
        var drops = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = document.Root;
        if (root is null || root.Name != Model + "definitions")
            return drops;

        var namedMessageIds = root.Elements(Model + "message")
            .Where(declaration => !string.IsNullOrWhiteSpace((string?)declaration.Attribute("name")))
            .Select(declaration => ((string?)declaration.Attribute("id"))?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal)!;

        foreach (var process in root.Elements(Model + "process"))
            ScanContainer(process, namedMessageIds!, drops);

        return drops;
    }

    private static void ScanContainer(XElement container, IReadOnlySet<string> namedMessageIds, Dictionary<string, string> drops)
    {
        var hostsById = container.Elements()
            .Where(child => child.Name.Namespace == Model && (string?)child.Attribute("id") is { Length: > 0 })
            .GroupBy(child => ((string?)child.Attribute("id"))!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var boundary in container.Elements(Model + "boundaryEvent"))
        {
            if (((string?)boundary.Attribute("id"))?.Trim() is not { Length: > 0 } id)
                continue;
            if (((string?)boundary.Attribute("attachedToRef"))?.Trim() is not { Length: > 0 } attachedToRef)
                continue;
            if (!hostsById.TryGetValue(attachedToRef, out var host) || !BoundaryHostLocalNames.Contains(host.Name.LocalName))
                continue;
            if (BindsElsaChild(host, namedMessageIds))
                continue;

            drops[id] = $"Boundary event '{id}' is attached to host '{attachedToRef}', which has no bound child activity to host a boundary, and was dropped.";
        }

        foreach (var nested in container.Elements().Where(child => child.Name == Model + "subProcess" || child.Name == Model + "transaction"))
            ScanContainer(nested, namedMessageIds, drops);
    }

    /// <summary>
    /// Whether Elsa binds a child activity to this element on import. A subprocess binds its nested process; a
    /// call activity binds a dispatch only when the document names the Elsa workflow to call; a send/receive
    /// task binds a publish/wait only when its <c>messageRef</c> resolves to a named declaration. Every other
    /// task imports childless.
    /// </summary>
    private static bool BindsElsaChild(XElement host, IReadOnlySet<string> namedMessageIds) => host.Name.LocalName switch
    {
        "subProcess" or "transaction" => true,
        "callActivity" => !string.IsNullOrWhiteSpace((string?)host.Attribute(BpmnVendorNamespace.Elsa + "workflowDefinitionId")),
        "sendTask" or "receiveTask" => ((string?)host.Attribute("messageRef"))?.Trim() is { Length: > 0 } messageRef && namedMessageIds.Contains(messageRef),
        _ => false
    };
}
