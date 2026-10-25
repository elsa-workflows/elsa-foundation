using System.Text.Json;
using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
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
public sealed class BpmnDocumentImporter(IBpmnWorkBinder workBinder, IActivityDefinitionLookup? catalog = null) : IBpmnDocumentImporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Placeholder activity version id for nested BPMN process nodes; API hosts override via options later.</summary>
    public const string DefaultBpmnProcessActivityVersionId = "Elsa.BpmnProcess";

    /// <summary>Placeholder activity version id for a timer catch event's synthesized <c>Delay</c> child (hosts resolve the real catalog row later).</summary>
    public const string DefaultDelayActivityVersionId = "Elsa.Delay";

    /// <summary>Placeholder activity version id for a message/signal catch event's synthesized <c>Event</c> child (matches <c>Event.ActivityType</c>).</summary>
    public const string DefaultEventActivityVersionId = "Elsa.Event";

    /// <summary>Placeholder activity version id for a bound call activity's synthesized <c>DispatchWorkflow</c> child (matches <c>DispatchWorkflow.ActivityType</c>). Hosts resolve the real catalog row later.</summary>
    public const string DefaultDispatchWorkflowActivityVersionId = "Elsa.DispatchWorkflow";

    /// <summary>Placeholder activity version id for a message throw/end or sendTask's synthesized <c>PublishEvent</c> child (matches <c>PublishEvent.ActivityType</c>). Hosts resolve the real catalog row later.</summary>
    public const string DefaultPublishEventActivityVersionId = "Elsa.PublishEvent";

    /// <summary>The element property key carrying a call activity's foreign BPMN <c>calledElement</c> id.</summary>
    public const string CalledElementPropertyKey = LibInterchange.BpmnXmlReader.CalledElementPropertyKey;

    /// <summary>The element property key carrying a sendTask/receiveTask's resolved message name.</summary>
    public const string MessageNamePropertyKey = LibInterchange.BpmnXmlReader.MessageNamePropertyKey;

    /// <summary>The Elsa extension attribute naming the workflow definition a bound call activity dispatches.</summary>
    private const string WorkflowDefinitionIdAttributeName = "workflowDefinitionId";

    public BpmnImportAnalysis Analyze(string xml, BpmnImportOptions? options = null) => Import(xml, options).Analysis;

    public BpmnImportResult Import(string xml, BpmnImportOptions? options = null) => BpmnCatalogBindingSession.Unverified(Read(xml, options));

    public async Task<BpmnImportAnalysis> AnalyzeAsync(string xml, BpmnImportOptions? options = null, CancellationToken cancellationToken = default) =>
        (await ImportAsync(xml, options, cancellationToken)).Analysis;

    public Task<BpmnImportResult> ImportAsync(string xml, BpmnImportOptions? options = null, CancellationToken cancellationToken = default) =>
        catalog is null ? Task.FromResult(Import(xml, options)) : new BpmnCatalogBindingSession(catalog).ResolveAsync(Read(xml, options), cancellationToken);

    /// <summary>
    /// The single code path behind both entry points, so a dry run can never disagree with the real one about
    /// what a document costs — including the findings this adapter contributes on top of the library's.
    /// </summary>
    private BpmnImportResult Read(string xml, BpmnImportOptions? options)
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

        return new ImportSession(read, boundaryDrops, options, workBinder).Build();
    }

    /// <summary>Holds the one read being mapped: the library's output, the boundaries Elsa must drop, and the findings the mapping adds.</summary>
    private sealed class ImportSession(
        LibInterchange.BpmnImportResult read,
        IReadOnlyDictionary<string, string> boundaryDrops,
        BpmnImportOptions? options,
        IBpmnWorkBinder binder)
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
        private readonly List<BpmnImportCapability> _capabilities = [];

        public BpmnImportResult Build()
        {
            ReportUnrepresented(read.Definitions.Extensions, read.Definitions.Id ?? "definitions");
            if (read.Definitions.Collaboration is { } retainedCollaboration)
            {
                ReportUnrepresented(retainedCollaboration.Extensions, retainedCollaboration.Id ?? "collaboration");
                foreach (var pool in retainedCollaboration.Pools)
                    ReportUnrepresented(pool.Extensions, pool.PoolId);
                foreach (var flow in retainedCollaboration.MessageFlows)
                    ReportUnrepresented(flow.Extensions, flow.FlowId);
            }
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
            return new BpmnImportResult(primary.Node, new BpmnImportAnalysis(read.Analysis.ProcessIds, read.Analysis.ElementCounts, _issues)
                { Capabilities = _capabilities }, imported);
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
            ReportUnrepresented(definition.Extensions, definition.ProcessId);
            foreach (var lane in definition.Lanes)
                ReportUnrepresented(lane.Extensions, lane.LaneId);
            if (definition.ExpressionLanguage is not null)
                Degraded(definition.ProcessId, "The process expression language is not represented by the Foundation authoring contract; expression evaluation is unavailable.");
            var childActivities = new List<ActivityNode>();
            var elements = new List<BpmnElement>();
            var dropped = new HashSet<string>(StringComparer.Ordinal);

            foreach (var source in definition.Elements)
            {
                ReportUnrepresented(source.Extensions with
                {
                    ForeignAttributes = source.Extensions.ForeignAttributes.Where(attribute =>
                        source.ElementType != BpmnElementTypes.CallActivity || !IsWorkflowDefinitionAttribute(attribute)).ToArray()
                }, source.ElementId);
                if (source.Script is not null)
                    Degraded(source.ElementId, "The script body and script format are not represented by the frozen Foundation authoring contract; bind an activity explicitly.");
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
                    RetainedProperties(source, definition),
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

                ReportUnrepresented(flow.Extensions, flow.FlowId);
                if (flow.ConditionExpression is not null)
                {
                    const string reason = "The typed condition is retained for edit/export in its source element's properties. Foundation cannot evaluate it and refuses publication/execution of this profile.";
                    Degraded(flow.FlowId, reason);
                    _capabilities.Add(new BpmnImportCapability(flow.FlowId, "ConditionEvaluation", BpmnImportCapabilityAvailability.Unsupported, reason));
                }
                var isDefault = flow.IsDefault || definition.Elements.Any(source => source.ElementId == flow.SourceRef && source.DefaultFlowId == flow.FlowId);
                flows.Add(new BpmnSequenceFlow(flow.FlowId, flow.SourceRef, flow.TargetRef, flow.Name,
                    flow.ConditionOutcome, isDefault));
            }

            foreach (var variable in definition.Variables)
                if (variable.TypeHint is not null || variable.DefaultValue is not null)
                    Degraded(definition.ProcessId, $"Variable '{variable.Name}' imports as Object without its library type hint or default value; those fields are unavailable in the frozen Foundation authoring contract.");

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

        private ActivityNode? MaterializeChild(LibInterchange.BpmnWorkBinding binding, LibModel.BpmnElement source)
        {
            var request = Request(binding, source);
            var result = binder.Bind(request) ?? throw new BpmnInterchangeException($"The work binder returned no assessment for '{source.ElementId}'.");
            if (result.Availability == BpmnImportCapabilityAvailability.Available && result.Activity is null ||
                result.Activity is not null && result.Activity.NodeId != binding.BindingRef)
                throw new BpmnInterchangeException($"The work binder returned an incompatible activity identity/availability for '{source.ElementId}'.");
            var capability = binding.GetType().Name;
            _capabilities.Add(new BpmnImportCapability(source.ElementId, capability, result.Availability, result.Reason) { BindingRef = binding.BindingRef });
            if (result.Availability is BpmnImportCapabilityAvailability.Unsupported or BpmnImportCapabilityAvailability.Unknown)
                Degraded(source.ElementId, $"{capability} was retained without executable work: {result.Reason}");
            if (request.Kind == BpmnWorkBindingKind.CallProcess && result.Availability == BpmnImportCapabilityAvailability.RequiresBinding)
                _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Info, result.Reason, source.ElementId));
            return result.Activity;
        }

        private BpmnWorkBindingRequest Request(LibInterchange.BpmnWorkBinding binding, LibModel.BpmnElement source) => binding switch
        {
            LibInterchange.BpmnWorkBinding.TimerWait timer => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.TimerWait, timer.IsoDuration),
            LibInterchange.BpmnWorkBinding.TimerDateWait date => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.TimerDateWait, date.IsoDate),
            LibInterchange.BpmnWorkBinding.TimerCycleWait cycle => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.TimerCycleWait, cycle.Cycle),
            LibInterchange.BpmnWorkBinding.MessageWait wait => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.MessageWait, wait.MessageName),
            LibInterchange.BpmnWorkBinding.SignalWait wait => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.SignalWait, wait.SignalName),
            LibInterchange.BpmnWorkBinding.MessagePublish publish => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.MessagePublish, publish.MessageName),
            LibInterchange.BpmnWorkBinding.SignalPublish publish => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.SignalPublish, publish.SignalName),
            LibInterchange.BpmnWorkBinding.CallProcess call => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.CallProcess,
                WorkflowDefinitionId: RetainedWorkflowDefinitionId(source), WaitForCompletion: call.WaitForCompletion),
            LibInterchange.BpmnWorkBinding.NestedProcess nested => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.NestedProcess,
                NestedProcess: BuildProcessNode(nested.Definition, binding.BindingRef, diagram: null, collaboration: null)),
            LibInterchange.BpmnWorkBinding.UnboundTask => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.UnboundTask),
            _ => new(source.ElementId, binding.BindingRef, BpmnWorkBindingKind.Unknown)
        };

        private void Degraded(string subject, string message) => _issues.Add(new BpmnImportIssue(BpmnImportIssueSeverity.Degraded, message, subject));

        private static IReadOnlyDictionary<string, string> RetainedProperties(LibModel.BpmnElement source, LibModel.BpmnProcessDefinition process)
        {
            var properties = new Dictionary<string, string>(source.Properties, StringComparer.Ordinal);
            foreach (var flow in process.SequenceFlows.Where(flow => flow.SourceRef == source.ElementId && flow.ConditionExpression is not null))
            {
                var expression = flow.ConditionExpression!;
                // Give an inherited language its equivalent explicit expression language, so export keeps
                // its meaning even though the frozen process envelope has no expressionLanguage field.
                var retained = new LibModel.BpmnConditionExpression(expression.Body, expression.Language ?? process.ExpressionLanguage, expression.Extensions);
                properties[BpmnRetainedAuthoring.ConditionExpressionPrefix + flow.FlowId] = JsonSerializer.Serialize(retained, SerializerOptions);
            }
            return properties;
        }

        private void ReportUnrepresented(LibModel.BpmnExtensions extensions, string subject)
        {
            if (!extensions.Equals(LibModel.BpmnExtensions.Empty))
                Degraded(subject, "Documentation, foreign attributes or extension content retained by the library are not fully represented in the frozen Foundation authoring contract; export cannot promise lossless retention.");
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
            .Where(attribute => IsWorkflowDefinitionAttribute(attribute)
                                && !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => attribute.Value.Trim())
            .FirstOrDefault();

    private static bool IsWorkflowDefinitionAttribute(LibModel.BpmnForeignAttribute attribute) =>
        attribute.Name.Namespace == LibInterchange.BpmnXmlNames.VendorNamespaceName && attribute.Name.LocalName == WorkflowDefinitionIdAttributeName;

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
