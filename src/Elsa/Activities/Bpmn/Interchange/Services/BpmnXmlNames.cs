using System.Xml.Linq;

namespace Elsa.Activities.Bpmn.Interchange.Services;

internal static class BpmnXmlNames
{
    public static readonly XNamespace Model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    public static readonly XNamespace Di = "http://www.omg.org/spec/BPMN/20100524/DI";
    public static readonly XNamespace Dc = "http://www.omg.org/spec/DD/20100524/DC";
    public static readonly XNamespace Dd = "http://www.omg.org/spec/DD/20100524/DI";

    /// <summary>
    /// Elsa vendor namespace for attributes that have no standard BPMN representation
    /// (outcome-matched flow conditions), so exports round-trip losslessly through this importer.
    /// </summary>
    public static readonly XNamespace Elsa = "https://elsa-workflows.io/schemas/bpmn";

    public static readonly IReadOnlyDictionary<string, string> TaskLocalNamesToElementTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["task"] = "task",
        ["userTask"] = "userTask",
        ["serviceTask"] = "serviceTask",
        ["scriptTask"] = "scriptTask",
        ["manualTask"] = "manualTask",
        ["businessRuleTask"] = "businessRuleTask",
        ["sendTask"] = "sendTask",
        ["receiveTask"] = "receiveTask"
    };

    public static readonly IReadOnlyDictionary<string, string> GatewayLocalNamesToElementTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["exclusiveGateway"] = "exclusiveGateway",
        ["parallelGateway"] = "parallelGateway",
        ["inclusiveGateway"] = "inclusiveGateway"
    };
}
