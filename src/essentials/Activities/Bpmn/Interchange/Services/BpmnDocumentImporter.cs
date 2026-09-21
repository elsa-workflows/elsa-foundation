using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using LibInterchange = global::Bpmn.Interchange;
using LibModel = global::Bpmn.Model;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// Imports BPMN 2.0 XML into the native <c>elsa.bpmn.structure</c> authored payload.
/// <para>
/// Reading the XML is <c>Bpmn.Interchange</c>'s job: it produces a neutral <see cref="LibModel.BpmnDefinitions"/>
/// plus one <see cref="LibInterchange.BpmnWorkBinding"/> per element that needs work performed, stated in BPMN
/// terms. This class is the half the library deliberately does not have — it decides which Elsa activity
/// performs each kind of work and lays the result out as an <see cref="ActivityNode"/> tree:
/// </para>
/// <list type="table">
/// <item><term>TimerWait</term><description><c>Elsa.Delay</c></description></item>
/// <item><term>MessageWait / SignalWait</term><description><c>Elsa.Event</c></description></item>
/// <item><term>MessagePublish</term><description><c>Elsa.PublishEvent</c></description></item>
/// <item><term>CallProcess</term><description><c>Elsa.DispatchWorkflow</c>, when the document names the Elsa workflow to call</description></item>
/// <item><term>NestedProcess</term><description>a nested <c>Elsa.BpmnProcess</c> node</description></item>
/// <item><term>UnboundTask</term><description>no child; an author binds one later</description></item>
/// </list>
/// <para>
/// The library's model renamed the two child references (<c>bindingRef</c>/<c>listenerBindingRef</c>) and made
/// the diagram a typed object. The authored payload keeps Elsa's names (<c>childNodeId</c>/<c>listenerNodeId</c>)
/// and the opaque <c>{shapes, edges}</c> diagram, because the runtime and the Studio's hand-maintained
/// TypeScript mirror both read it; the translation lives here.
/// </para>
/// </summary>
public sealed class BpmnDocumentImporter : IBpmnDocumentImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Placeholder activity version id for nested BPMN process nodes; API hosts override via options later.</summary>
    public const string DefaultBpmnProcessActivityVersionId = "Elsa.BpmnProcess";

    /// <summary>Placeholder activity version id for a timer catch event's synthesized <c>Delay</c> child (hosts resolve the real catalog row later).</summary>
    public const string DefaultDelayActivityVersionId = "Elsa.Delay";

    /// <summary>Placeholder activity version id for a message/signal catch event's synthesized <c>Event</c> child (matches <c>Event.ActivityType</c>).</summary>
    public const string DefaultEventActivityVersionId = "Elsa.Event";

    /// <summary>Placeholder activity version id for a bound call activity's synthesized <c>DispatchWorkflow</c> child (spec 133 D5; matches <c>DispatchWorkflow.ActivityType</c>). Hosts resolve the real catalog row later.</summary>
    public const string DefaultDispatchWorkflowActivityVersionId = "Elsa.DispatchWorkflow";

    /// <summary>Placeholder activity version id for a message throw/end or sendTask's synthesized <c>PublishEvent</c> child (spec 135; matches <c>PublishEvent.ActivityType</c>). Hosts resolve the real catalog row later.</summary>
    public const string DefaultPublishEventActivityVersionId = "Elsa.PublishEvent";

    /// <summary>The element property key carrying a call activity's foreign BPMN <c>calledElement</c> id (spec 133 D5).</summary>
    public const string CalledElementPropertyKey = LibInterchange.BpmnXmlReader.CalledElementPropertyKey;

    /// <summary>The element property key carrying a sendTask/receiveTask's resolved message name (spec 135).</summary>
    public const string MessageNamePropertyKey = LibInterchange.BpmnXmlReader.MessageNamePropertyKey;

    /// <summary>The Elsa extension attribute naming the workflow definition a bound call activity dispatches.</summary>
    private const string WorkflowDefinitionIdAttributeName = "workflowDefinitionId";

    public BpmnImportAnalysis Analyze(string xml, BpmnImportOptions? options = null) => Read(xml, options).Analysis;

    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null) => Read(xml, options);

    /// <summary>
    /// The single code path behind both entry points, so a dry run can never disagree with the real one about
    /// what a document costs — including the findings this adapter contributes on top of the library's.
    /// </summary>
    private static BpmnImportResult Read(string xml, BpmnImportOptions? options)
    {
        var document = BpmnVendorNamespace.TryParse(xml);
        IReadOnlyDictionary<string, string> boundaryDrops = document is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : BpmnUnboundHostPolicy.ScanBoundariesOnUnboundHosts(document);
        var readable = document is not null && xml.Contains(BpmnVendorNamespace.NamespaceName, StringComparison.Ordinal)
            ? BpmnVendorNamespace.ToLibrary(document)
            : xml;

        LibInterchange.BpmnImportResult read;
        try
        {
            read = new LibInterchange.BpmnXmlReader().Read(readable, new LibInterchange.BpmnImportOptions
            {
                ProcessId = options?.ProcessId
            });
        }
        catch (LibInterchange.BpmnInterchangeException exception)
        {
            throw new BpmnInterchangeException(exception.Message, exception);
        }

        return new ImportSession(read, boundaryDrops, options).Build();
    }

    /// <summary>Holds the one read being mapped: the library's output, the boundaries Elsa must drop, and the findings the mapping adds.</summary>
    private sealed class ImportSession(
        LibInterchange.BpmnImportResult read,
        IReadOnlyDictionary<string, string> boundaryDrops,
        BpmnImportOptions? options)
    {
        private readonly Dictionary<string, LibInterchange.BpmnWorkBinding> _bindings =
            read.Bindings.ToDictionary(binding => binding.BindingRef, StringComparer.Ordinal);

        private readonly List<BpmnImportIssue> _issues = read.Analysis.Issues
            // A boundary Elsa drops for its own reason must not also carry the library's differently-reasoned
            // finding for the same element; suppressing by id avoids matching on message text.
            .Where(issue => issue.ElementId is null || !boundaryDrops.ContainsKey(issue.ElementId))
            // Elsa's finding names a single subject. The library split that into an element and a process, so a
            // process-scoped finding (a non-executable pool, ambiguous lane ownership) falls back to the process id
            // — which is exactly what Elsa put in ElementId before the split existed.
            .Select(issue => new BpmnImportIssue(Map(issue.Severity), issue.Message, issue.ElementId ?? issue.ProcessId))
            .ToList();

        public BpmnImportResult Build()
        {
            foreach (var (elementId, message) in boundaryDrops)
                _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, message, elementId));

            var definitions = read.Definitions;
            var diagram = ReadDiagram(definitions);
            var collaboration = new CollaborationFacts(definitions);
            var nodePrefix = options?.NodeIdPrefix ?? LibInterchange.BpmnXmlReader.DefaultBindingRefPrefix;

            var imported = definitions.Processes
                .Select(process => new BpmnImportedProcess(
                    process.ProcessId,
                    collaboration.ParticipantIdFor(process.ProcessId),
                    collaboration.ParticipantNameFor(process.ProcessId),
                    BuildProcessNode(process, $"{nodePrefix}-{process.ProcessId}", diagram, collaboration)))
                .ToArray();

            var primary = SelectPrimary(definitions.Processes, imported);
            return new BpmnImportResult(primary.Node, new BpmnImportAnalysis(read.Analysis.ProcessIds, read.Analysis.ElementCounts, _issues), imported);
        }

        /// <summary>Explicit <c>ProcessId</c> &gt; first executable &gt; first, unchanged from before the library.</summary>
        private BpmnImportedProcess SelectPrimary(IReadOnlyList<LibModel.BpmnProcessDefinition> processes, IReadOnlyList<BpmnImportedProcess> imported)
        {
            if (options?.ProcessId is { } requested)
                return imported.First(entry => StringComparer.Ordinal.Equals(entry.ProcessId, requested));

            var executable = processes.Select((process, index) => (process, index)).FirstOrDefault(entry => entry.process.IsExecutable);
            return imported[executable.process is null ? 0 : executable.index];
        }

        private ActivityNode BuildProcessNode(
            LibModel.BpmnProcessDefinition definition,
            string nodeId,
            JsonElement? diagram,
            CollaborationFacts? collaboration)
        {
            var childActivities = new List<ActivityNode>();
            var elements = new List<BpmnElement>();
            var dropped = new HashSet<string>(StringComparer.Ordinal);

            foreach (var source in definition.Elements)
            {
                if (boundaryDrops.ContainsKey(source.ElementId))
                {
                    dropped.Add(source.ElementId);
                    continue;
                }

                var child = source.BindingRef is { } bindingRef && _bindings.TryGetValue(bindingRef, out var binding)
                    ? MaterializeChild(binding, source)
                    : null;
                if (child is not null)
                    childActivities.Add(child);

                var listener = source.ListenerBindingRef is { } listenerRef && _bindings.TryGetValue(listenerRef, out var listenerBinding)
                    ? MaterializeChild(listenerBinding, source)
                    : null;
                if (listener is not null)
                    childActivities.Add(listener);

                elements.Add(new BpmnElement(
                    source.ElementId,
                    source.ElementType,
                    source.Name,
                    child?.NodeId,
                    source.LaneId,
                    source.DefaultFlowId,
                    source.EventDefinitions.Select(definitionEntry => new BpmnEventDefinition(definitionEntry.Type, definitionEntry.Properties)).ToArray(),
                    source.Properties.Count == 0 ? null : source.Properties,
                    source.AttachedToRef,
                    source.CancelActivity,
                    ResolveLoopCharacteristics(source, child is not null),
                    source.IsForCompensation,
                    source.CompensationHandlerElementId,
                    source.IsTransaction,
                    source.TriggeredByEvent,
                    listener?.NodeId));
            }

            var flows = new List<BpmnSequenceFlow>();
            foreach (var flow in definition.SequenceFlows)
            {
                if (dropped.Contains(flow.SourceRef) || dropped.Contains(flow.TargetRef))
                {
                    _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Dropped, $"Sequence flow '{flow.FlowId}' references a dropped element and was dropped with it.", flow.FlowId));
                    continue;
                }

                flows.Add(new BpmnSequenceFlow(flow.FlowId, flow.SourceRef, flow.TargetRef, flow.Name, flow.ConditionOutcome));
            }

            var structure = new BpmnAuthoredStructure(
                activities: childActivities,
                elements: elements,
                sequenceFlows: flows,
                pools: collaboration?.PoolsFor(definition.ProcessId) ?? [],
                lanes: definition.Lanes.Select(lane => new BpmnLane(lane.LaneId, lane.PoolId, lane.Name)).ToArray(),
                variables: definition.Variables
                    .Select(variable => new VariableDefinition(variable.Name, variable.Name, new TypeReference("Object"), StorageDriverType: null, Default: null))
                    .ToArray(),
                diagram: diagram,
                isTransaction: definition.IsTransaction,
                messageFlows: collaboration?.MessageFlowsFor(definition.ProcessId) ?? []);

            return new ActivityNode(
                nodeId,
                DefaultBpmnProcessActivityVersionId,
                [],
                [],
                new ActivityNodeStructure(
                    BpmnProcessActivity.StructureKind,
                    BpmnProcessActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(structure, SerializerOptions)));
        }

        /// <summary>Binds one declared unit of work to the Elsa activity that performs it; <c>null</c> when Elsa binds nothing.</summary>
        private ActivityNode? MaterializeChild(LibInterchange.BpmnWorkBinding binding, LibModel.BpmnElement source) => binding switch
        {
            LibInterchange.BpmnWorkBinding.TimerWait timer =>
                new(binding.BindingRef, DefaultDelayActivityVersionId, [Literal("Duration", timer.IsoDuration)], []),
            LibInterchange.BpmnWorkBinding.MessageWait wait => EventCatch(binding.BindingRef, wait.MessageName),
            LibInterchange.BpmnWorkBinding.SignalWait signal => EventCatch(binding.BindingRef, signal.SignalName),
            LibInterchange.BpmnWorkBinding.MessagePublish publish =>
                new(binding.BindingRef, DefaultPublishEventActivityVersionId, [Literal("EventName", publish.MessageName)], []),
            LibInterchange.BpmnWorkBinding.NestedProcess nested =>
                BuildProcessNode(nested.Definition, binding.BindingRef, diagram: null, collaboration: null),
            LibInterchange.BpmnWorkBinding.CallProcess call => MaterializeCall(call, source),
            _ => null
        };

        /// <summary>
        /// A call activity binds a <c>DispatchWorkflow</c> only when the document names the Elsa workflow to call
        /// (our <c>elsa:workflowDefinitionId</c> export convention). A foreign <c>calledElement</c> has no
        /// guaranteed Elsa definition id, so it imports unbound with an Info finding — the serviceTask precedent.
        /// </summary>
        private ActivityNode? MaterializeCall(LibInterchange.BpmnWorkBinding.CallProcess call, LibModel.BpmnElement source)
        {
            if (RetainedWorkflowDefinitionId(source) is not { } workflowDefinitionId)
            {
                _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Info, $"Call activity '{call.ElementId}' imported unbound; bind a DispatchWorkflow activity to execute it.", call.ElementId));
                return null;
            }

            return new(call.BindingRef, DefaultDispatchWorkflowActivityVersionId,
                [Literal("WorkflowDefinitionId", workflowDefinitionId), Literal("WaitForCompletion", call.WaitForCompletion)], []);
        }

        /// <summary>
        /// Elsa's multi-instance rule is narrower than the library's: a loop needs a bound child to run, so a
        /// childless host degrades to no loop rather than emitting a loop the graph validator rejects.
        /// </summary>
        private BpmnLoopCharacteristics? ResolveLoopCharacteristics(LibModel.BpmnElement source, bool bindsChild)
        {
            if (source.LoopCharacteristics is not { } loop)
                return null;

            if (!bindsChild)
            {
                _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, $"Element '{source.ElementId}' declares multi-instance loop characteristics but binds no child on import (only a bound subprocess can host a loop); it imported without loop characteristics.", source.ElementId));
                return null;
            }

            return new BpmnLoopCharacteristics(loop.IsSequential, loop.Cardinality, loop.CollectionVariable, loop.ItemVariable);
        }
    }

    /// <summary>The <c>elsa:workflowDefinitionId</c> the library retained as a foreign attribute, if the document carried one.</summary>
    private static string? RetainedWorkflowDefinitionId(LibModel.BpmnElement source) =>
        source.Extensions.ForeignAttributes
            .Where(attribute => StringComparer.Ordinal.Equals(attribute.Name.LocalName, WorkflowDefinitionIdAttributeName)
                                && !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => attribute.Value.Trim())
            .FirstOrDefault();

    /// <summary>A message/signal wait's child: a mid-flow <c>Event</c> (<c>CanStartWorkflow = false</c>).</summary>
    private static ActivityNode EventCatch(string nodeId, string eventName) =>
        new(nodeId, DefaultEventActivityVersionId, [Literal("EventName", eventName), Literal("CanStartWorkflow", false)], []);

    /// <summary>Authors a single literal input binding for a synthesized child node.</summary>
    private static ArgumentState Literal(string key, object? value) => new(key, new ArgumentValue(value, "Literal"), null, null, null, null);

    private static BpmnImportIssueSeverity Map(LibInterchange.BpmnImportIssueSeverity severity) => severity switch
    {
        LibInterchange.BpmnImportIssueSeverity.Degraded => BpmnImportIssueSeverity.Degraded,
        LibInterchange.BpmnImportIssueSeverity.Dropped => BpmnImportIssueSeverity.Dropped,
        _ => BpmnImportIssueSeverity.Info
    };

    /// <summary>
    /// Flattens the library's typed <see cref="LibModel.BpmnDiagram"/> back to the opaque <c>{shapes, edges}</c>
    /// payload design surfaces (and the Studio's TypeScript mirror) read.
    /// </summary>
    private static JsonElement? ReadDiagram(LibModel.BpmnDefinitions definitions)
    {
        if (definitions.Diagrams.FirstOrDefault()?.Plane is not { } plane)
            return null;

        var shapes = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var shape in plane.Shapes)
            shapes[shape.BpmnElementRef] = new { x = shape.Bounds.X, y = shape.Bounds.Y, width = shape.Bounds.Width, height = shape.Bounds.Height };

        var edges = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var edge in plane.Edges)
            edges[edge.BpmnElementRef] = new { waypoints = edge.Waypoints.Select(waypoint => new { x = waypoint.X, y = waypoint.Y }).ToArray() };

        return JsonSerializer.SerializeToElement(new { shapes, edges }, SerializerOptions);
    }

    /// <summary>
    /// The document's collaboration, indexed the way the authored payload needs it: pools per referenced process,
    /// and each message flow recorded on the structure of every process an endpoint of it lives in.
    /// </summary>
    private sealed class CollaborationFacts
    {
        private readonly Dictionary<string, List<BpmnPool>> _poolsByProcessId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<BpmnMessageFlow>> _flowsByProcessId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LibModel.BpmnPool> _soleParticipantByProcessId = new(StringComparer.Ordinal);

        public CollaborationFacts(LibModel.BpmnDefinitions definitions)
        {
            var collaboration = definitions.Collaboration;
            if (collaboration is null)
                return;

            foreach (var pool in collaboration.Pools.Where(pool => pool.ProcessRef is not null))
                Bucket(_poolsByProcessId, pool.ProcessRef!).Add(new BpmnPool(pool.PoolId, pool.Name, pool.ProcessRef, pool.IsExecutable));

            foreach (var (processId, pools) in collaboration.Pools.Where(pool => pool.ProcessRef is not null).GroupBy(pool => pool.ProcessRef!, StringComparer.Ordinal).Select(group => (group.Key, group.ToArray())))
                if (pools.Length == 1)
                    _soleParticipantByProcessId[processId] = pools[0];

            var processByElementId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var process in definitions.Processes)
                foreach (var element in process.Elements)
                    processByElementId.TryAdd(element.ElementId, process.ProcessId);

            foreach (var flow in collaboration.MessageFlows)
            {
                var record = new BpmnMessageFlow(flow.FlowId, flow.Name, flow.SourceElementId, flow.SourcePoolId, flow.TargetElementId, flow.TargetPoolId, flow.MessageName);
                foreach (var endpoint in new[] { flow.SourceElementId, flow.TargetElementId })
                    if (endpoint is not null && processByElementId.TryGetValue(endpoint, out var owner))
                        Bucket(_flowsByProcessId, owner).Add(record);
            }
        }

        public IReadOnlyCollection<BpmnPool> PoolsFor(string processId) =>
            _poolsByProcessId.TryGetValue(processId, out var pools) ? pools : [];

        public IReadOnlyCollection<BpmnMessageFlow> MessageFlowsFor(string processId) =>
            _flowsByProcessId.TryGetValue(processId, out var flows) ? flows : [];

        public string? ParticipantIdFor(string processId) =>
            _soleParticipantByProcessId.TryGetValue(processId, out var pool) ? pool.PoolId : null;

        public string? ParticipantNameFor(string processId) =>
            _soleParticipantByProcessId.TryGetValue(processId, out var pool) ? pool.Name : null;

        private static List<T> Bucket<T>(Dictionary<string, List<T>> buckets, string key) =>
            buckets.TryGetValue(key, out var bucket) ? bucket : buckets[key] = [];
    }
}
