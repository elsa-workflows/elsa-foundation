using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

public interface IWorkflowBuilder<TRequest, TResult> : ISequenceBuilder
{
    WorkflowRequestSource<TRequest> Request { get; }
    ISequenceBuilder Sequence { get; }
    WorkflowRequestMember<T> From<T>(System.Linq.Expressions.Expression<Func<TRequest, T>> member);
    WorkflowValue<T> Value<T>(T? literal);
    ExpressionSource<T> Expression<T>(string language, string source);
    void Return(WorkflowValue<TResult> result);
}

public interface ISequenceBuilder
{
    string ScopeId { get; }
    Variable<T> Variable<T>(string name, T? initialValue = default);
    void Set<T>(Variable<T> variable, WorkflowValue<T> value);
    void Merge<T>(Variable<T> variable, WorkflowValue<T> mergedValue);
    void Reduce<T>(Variable<T> variable, WorkflowValue<T> reducedValue);
    void SetCorrelationId(WorkflowValue<string> correlationId);
    void SetInstanceName(WorkflowValue<string> instanceName);
    void SetOutput<T>(string outputKey, WorkflowValue<T> value);
    void Finish(string outcomeKey = "Done");
    void Return<T>(WorkflowValue<T> result);
    void Control(string outcomeKey);
    ActivityCall<TResult> Add<TActivity, TResult>(
        string activityVersionId,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null);

    ActivityCall<TResult> AddStructured<TActivity, TResult>(
        string activityVersionId,
        Func<IActivityStructureBuilder, ActivityNodeStructure> structure,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null);
}

/// <summary>
/// Authoring-only context supplied by a structured activity extension. The owning activity builds
/// its own persisted structure payload and may request lexical sequence regions for its child slots.
/// </summary>
public interface IActivityStructureBuilder
{
    string NodeId { get; }
    string ScopeId { get; }

    ActivityNode Sequence(
        string regionKey,
        Action<ISequenceBuilder> build,
        string? nodeId = null,
        string activityVersionId = "elsa.sequence@1");
}

public interface IActivityInputBuilder<TActivity>
{
    IActivityInputBuilder<TActivity> Set<T>(string inputKey, ActivityArgument<T> argument);
    IActivityInputBuilder<TActivity> From<T>(string inputKey, WorkflowValue<T> source);
    IActivityInputBuilder<TActivity> Value<T>(string inputKey, T? literal);
}

public sealed class WorkflowRequestSource<TRequest>
{
    public WorkflowRequestMember<T> Member<T>(string memberKey, string? path = null) => new(memberKey, path);
}

public sealed class Variable<T>
{
    internal Variable(string referenceKey, string name, string declaringScopeId)
    {
        ReferenceKey = referenceKey;
        Name = name;
        Value = new VariableRead<T>(referenceKey, declaringScopeId);
    }

    public string ReferenceKey { get; }
    public string Name { get; }
    public VariableRead<T> Value { get; }
}

public sealed record ActivityNodeHandle(string NodeId, string ScopeId);

public sealed record ActivityOutcomeSource(ActivityNodeHandle Node, string OutcomeKey);

public sealed class ActivityCall<TResult>
{
    internal ActivityCall(ActivityNodeHandle node)
    {
        Node = node;
        Result = new ActivityResultSource<TResult>(node.NodeId, "$result", node.ScopeId);
    }

    public ActivityNodeHandle Node { get; }
    public ActivityResultSource<TResult> Result { get; }
    public ActivityResultSource<T> Output<T>(string projectionKey) => new(Node.NodeId, projectionKey, Node.ScopeId);
    public ActivityOutcomeSource Outcome(string outcomeKey) => new(Node, outcomeKey);
}

internal sealed class WorkflowBuilder<TRequest, TResult> : IWorkflowBuilder<TRequest, TResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal) { "root" };
    private readonly SequenceBuilder _root;
    private int _nodeOrdinal;

    public WorkflowBuilder() => _root = new SequenceBuilder(this, "root", null, isWorkflowRoot: true);

    public WorkflowRequestSource<TRequest> Request { get; } = new();
    public string ScopeId => _root.ScopeId;
    public ISequenceBuilder Sequence => _root;

    public Variable<T> Variable<T>(string name, T? initialValue = default) => _root.Variable(name, initialValue);

    public WorkflowRequestMember<T> From<T>(System.Linq.Expressions.Expression<Func<TRequest, T>> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var path = ReadMemberPath(member.Body);
        var memberKey = ToStableKey(path.Split('.')[0]);
        return new WorkflowRequestMember<T>(memberKey, path);
    }

    public WorkflowValue<T> Value<T>(T? literal) => new LiteralWorkflowValue<T>(literal);

    public ExpressionSource<T> Expression<T>(string language, string source) => new(language, source);

    public void Set<T>(Variable<T> variable, WorkflowValue<T> value)
        => _root.Set(variable, value);

    public void Merge<T>(Variable<T> variable, WorkflowValue<T> mergedValue)
        => _root.Merge(variable, mergedValue);

    public void Reduce<T>(Variable<T> variable, WorkflowValue<T> reducedValue)
        => _root.Reduce(variable, reducedValue);

    public void SetCorrelationId(WorkflowValue<string> correlationId)
        => _root.SetCorrelationId(correlationId);

    public void SetInstanceName(WorkflowValue<string> instanceName)
        => _root.SetInstanceName(instanceName);

    public void SetOutput<T>(string outputKey, WorkflowValue<T> value)
        => _root.SetOutput(outputKey, value);

    public void Finish(string outcomeKey = "Done")
        => _root.Finish(outcomeKey);

    public void Control(string outcomeKey)
        => _root.Control(outcomeKey);

    public ActivityCall<TActivityResult> Add<TActivity, TActivityResult>(
        string activityVersionId,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null) => _root.Add<TActivity, TActivityResult>(activityVersionId, inputs, nodeId);

    public ActivityCall<TActivityResult> AddStructured<TActivity, TActivityResult>(
        string activityVersionId,
        Func<IActivityStructureBuilder, ActivityNodeStructure> structure,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null) => _root.AddStructured<TActivity, TActivityResult>(activityVersionId, structure, inputs, nodeId);

    public void Return(WorkflowValue<TResult> result)
        => _root.Return(result);

    public void Return<T>(WorkflowValue<T> result)
        => _root.Return(result);

    internal WorkflowDefinitionState BuildState()
    {
        // The current publisher projects workflow variables from the root Sequence structure while
        // WorkflowDefinitionState also owns the declarations at document scope. Keep the mirrored
        // authored representation until the publisher consumes the document-level declarations.
        var root = _root.Lower("root", "elsa.sequence@1");
        return new WorkflowDefinitionState(_root.Variables.ToArray(), root, [], [], null, null);
    }

    private string AllocateNodeId(Type activityType, string? requestedNodeId)
    {
        var ordinal = ++_nodeOrdinal;
        var nodeId = requestedNodeId ?? $"{ToStableKey(activityType.Name)}-{ordinal}";
        if (!_nodeIds.Add(nodeId))
            throw new InvalidOperationException($"Activity node id '{nodeId}' is already used.");
        return nodeId;
    }

    private string AllocateIntrinsicNodeId()
    {
        var ordinal = ++_nodeOrdinal;
        var nodeId = $"intrinsic-{ordinal}";
        if (!_nodeIds.Add(nodeId))
            throw new InvalidOperationException($"Activity node id '{nodeId}' is already used.");
        return nodeId;
    }

    private string AllocateRegionNodeId(string ownerNodeId, string regionKey, string? requestedNodeId)
    {
        var nodeId = requestedNodeId ?? $"{ownerNodeId}-{ToStableKey(regionKey)}";
        if (!_nodeIds.Add(nodeId))
            throw new InvalidOperationException($"Activity node id '{nodeId}' is already used.");
        return nodeId;
    }

    private static string ReadMemberPath(System.Linq.Expressions.Expression expression)
    {
        var members = new Stack<string>();
        while (expression is System.Linq.Expressions.MemberExpression member)
        {
            members.Push(member.Member.Name);
            expression = member.Expression!;
        }

        if (expression is not System.Linq.Expressions.ParameterExpression || members.Count == 0)
            throw new ArgumentException("A workflow request source must be a direct request member path.");
        return string.Join('.', members);
    }

    private static string ToStableKey(string value)
    {
        var characters = value.SelectMany((character, index) =>
            char.IsUpper(character) && index > 0 ? new[] { '-', char.ToLowerInvariant(character) } : new[] { char.ToLowerInvariant(character) });
        return new string(characters.ToArray()).Replace('_', '-');
    }

    private sealed class SequenceBuilder(
        WorkflowBuilder<TRequest, TResult> owner,
        string scopeId,
        SequenceBuilder? parent,
        bool isWorkflowRoot = false) : ISequenceBuilder
    {
        private readonly List<ActivityNode> _activities = [];
        private readonly List<VariableDefinition> _variables = [];

        public string ScopeId { get; } = scopeId;
        public string VariableScopeId => isWorkflowRoot ? VariableReference.WorkflowScopeId : ScopeId;
        public IReadOnlyCollection<VariableDefinition> Variables => _variables;

        public Variable<T> Variable<T>(string name, T? initialValue = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var referenceKey = ToStableKey(name);
            if (_variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey)))
                throw new InvalidOperationException($"Variable key '{referenceKey}' is already declared in scope '{ScopeId}'.");

            _variables.Add(new VariableDefinition(
                referenceKey,
                name,
                TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias),
                StorageDriverType: null,
                Default: new ArgumentValue(initialValue, AuthoringExpressionTypes.Literal)));
            return new Variable<T>(referenceKey, name, VariableScopeId);
        }

        public void Set<T>(Variable<T> variable, WorkflowValue<T> value)
            => AddVariableWriteIntrinsic(AuthoredWorkflowIntrinsicKind.Set, variable, value);

        public void Merge<T>(Variable<T> variable, WorkflowValue<T> mergedValue)
            => AddVariableWriteIntrinsic(AuthoredWorkflowIntrinsicKind.Merge, variable, mergedValue);

        public void Reduce<T>(Variable<T> variable, WorkflowValue<T> reducedValue)
            => AddVariableWriteIntrinsic(AuthoredWorkflowIntrinsicKind.Reduce, variable, reducedValue);

        public void SetCorrelationId(WorkflowValue<string> correlationId)
            => AddValueEffectIntrinsic(AuthoredWorkflowIntrinsicKind.SetCorrelationId, correlationId);

        public void SetInstanceName(WorkflowValue<string> instanceName)
            => AddValueEffectIntrinsic(AuthoredWorkflowIntrinsicKind.SetInstanceName, instanceName);

        public void SetOutput<T>(string outputKey, WorkflowValue<T> value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputKey);
            ArgumentNullException.ThrowIfNull(value);
            EnsureVisible(value.Scope);
            AddIntrinsic(
                WorkflowIntrinsicAuthoringIds.SetOutput,
                new AuthoredWorkflowIntrinsic(
                    AuthoredWorkflowIntrinsicKind.SetOutput,
                    TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias)),
                [
                    new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Name, new ArgumentValue(outputKey, AuthoringExpressionTypes.Literal), null, null, null, null),
                    new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Value, value.Lower(), null, null, null, null)
                ]);
        }

        public void Finish(string outcomeKey = "Done")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcomeKey);
            AddIntrinsic(
                WorkflowIntrinsicAuthoringIds.Finish,
                new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Finish),
                [new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Outcome, new ArgumentValue(outcomeKey, AuthoringExpressionTypes.Literal), null, null, null, null)]);
        }

        public void Return<T>(WorkflowValue<T> result)
        {
            ArgumentNullException.ThrowIfNull(result);
            EnsureVisible(result.Scope);
            AddIntrinsic(
                WorkflowIntrinsicAuthoringIds.Return,
                new AuthoredWorkflowIntrinsic(
                    AuthoredWorkflowIntrinsicKind.Return,
                    TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias)),
                [new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Value, result.Lower(), null, null, null, null)]);
        }

        public void Control(string outcomeKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcomeKey);
            AddIntrinsic(
                WorkflowIntrinsicAuthoringIds.Control,
                new AuthoredWorkflowIntrinsic(AuthoredWorkflowIntrinsicKind.Control),
                [new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Outcome, new ArgumentValue(outcomeKey, AuthoringExpressionTypes.Literal), null, null, null, null)]);
        }

        public ActivityCall<TActivityResult> Add<TActivity, TActivityResult>(
            string activityVersionId,
            Action<IActivityInputBuilder<TActivity>>? inputs = null,
            string? nodeId = null) => AddCore<TActivity, TActivityResult>(activityVersionId, null, inputs, nodeId);

        public ActivityCall<TActivityResult> AddStructured<TActivity, TActivityResult>(
            string activityVersionId,
            Func<IActivityStructureBuilder, ActivityNodeStructure> structure,
            Action<IActivityInputBuilder<TActivity>>? inputs = null,
            string? nodeId = null)
        {
            ArgumentNullException.ThrowIfNull(structure);
            return AddCore<TActivity, TActivityResult>(activityVersionId, structure, inputs, nodeId);
        }

        public ActivityNode Lower(string nodeId, string activityVersionId)
        {
            var payload = JsonSerializer.SerializeToElement(new { activities = _activities, variables = _variables }, JsonOptions);
            return new ActivityNode(
                nodeId,
                activityVersionId,
                Inputs: [],
                Outputs: [],
                new ActivityNodeStructure("elsa.sequence.structure", "1.0.0", payload));
        }

        public bool ContainsScope(string candidateScopeId)
        {
            var normalized = StringComparer.Ordinal.Equals(candidateScopeId, VariableReference.WorkflowScopeId) ? "root" : candidateScopeId;
            for (var current = this; current is not null; current = current.Parent)
            {
                if (StringComparer.Ordinal.Equals(current.ScopeId, normalized))
                    return true;
            }

            return false;
        }

        private SequenceBuilder? Parent => parent;

        private ActivityCall<TActivityResult> AddCore<TActivity, TActivityResult>(
            string activityVersionId,
            Func<IActivityStructureBuilder, ActivityNodeStructure>? structure,
            Action<IActivityInputBuilder<TActivity>>? inputs,
            string? requestedNodeId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(activityVersionId);
            var nodeId = owner.AllocateNodeId(typeof(TActivity), requestedNodeId);
            var inputBuilder = new ActivityInputBuilder<TActivity>(this);
            inputs?.Invoke(inputBuilder);
            var authoredStructure = structure?.Invoke(new ActivityStructureBuilder(owner, this, nodeId));
            _activities.Add(new ActivityNode(nodeId, activityVersionId, inputBuilder.Build(), [], authoredStructure));
            var handle = new ActivityNodeHandle(nodeId, ScopeId);
            return new ActivityCall<TActivityResult>(handle);
        }

        private void AddVariableWriteIntrinsic<T>(
            AuthoredWorkflowIntrinsicKind kind,
            Variable<T> variable,
            WorkflowValue<T> value)
        {
            ArgumentNullException.ThrowIfNull(variable);
            ArgumentNullException.ThrowIfNull(value);
            EnsureVisible(new WorkflowValueScope(WorkflowValueScopeKind.Variable, variable.Value.DeclaringScopeId));
            EnsureVisible(value.Scope);
            var id = kind switch
            {
                AuthoredWorkflowIntrinsicKind.Set => WorkflowIntrinsicAuthoringIds.Set,
                AuthoredWorkflowIntrinsicKind.Merge => WorkflowIntrinsicAuthoringIds.Merge,
                AuthoredWorkflowIntrinsicKind.Reduce => WorkflowIntrinsicAuthoringIds.Reduce,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Intrinsic is not a variable write.")
            };
            AddIntrinsic(
                id,
                new AuthoredWorkflowIntrinsic(
                    kind,
                    TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias),
                    new VariableReference(variable.ReferenceKey, variable.Value.DeclaringScopeId)),
                [new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Value, value.Lower(), null, null, null, null)]);
        }

        private void AddValueEffectIntrinsic<T>(AuthoredWorkflowIntrinsicKind kind, WorkflowValue<T> value)
        {
            ArgumentNullException.ThrowIfNull(value);
            EnsureVisible(value.Scope);
            var id = kind switch
            {
                AuthoredWorkflowIntrinsicKind.SetCorrelationId => WorkflowIntrinsicAuthoringIds.SetCorrelationId,
                AuthoredWorkflowIntrinsicKind.SetInstanceName => WorkflowIntrinsicAuthoringIds.SetInstanceName,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Intrinsic is not a single-value workflow effect.")
            };
            AddIntrinsic(
                id,
                new AuthoredWorkflowIntrinsic(
                    kind,
                    TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias)),
                [new ArgumentState(WorkflowIntrinsicAuthoringInputKeys.Value, value.Lower(), null, null, null, null)]);
        }

        private void AddIntrinsic(
            string activityVersionId,
            AuthoredWorkflowIntrinsic intrinsic,
            IReadOnlyCollection<ArgumentState> inputs)
        {
            var nodeId = owner.AllocateIntrinsicNodeId();
            _activities.Add(new ActivityNode(nodeId, activityVersionId, inputs, []) { Intrinsic = intrinsic });
        }

        private void EnsureVisible(WorkflowValueScope source)
        {
            if (source.Kind == WorkflowValueScopeKind.Unscoped || source.ScopeId is null || ContainsScope(source.ScopeId))
                return;

            var code = source.Kind == WorkflowValueScopeKind.Variable ? "VF-AUTH-003" : "VF-AUTH-004";
            var noun = source.Kind == WorkflowValueScopeKind.Variable ? "Variable" : "Activity result";
            throw new InvalidOperationException($"{code}: {noun} declared in scope '{source.ScopeId}' is not visible from scope '{ScopeId}'.");
        }

        private sealed class ActivityInputBuilder<TActivity>(SequenceBuilder sequence) : IActivityInputBuilder<TActivity>
        {
            private readonly Dictionary<string, ArgumentState> _inputs = new(StringComparer.Ordinal);

            public IActivityInputBuilder<TActivity> Set<T>(string inputKey, ActivityArgument<T> argument)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
                if (argument.Kind == ActivityArgumentKind.Omitted)
                    return this;
                if (argument.Kind == ActivityArgumentKind.Source)
                    sequence.EnsureVisible(argument.Source!.Scope);
                _inputs[inputKey] = new ArgumentState(inputKey, argument.Lower(inputKey), null, null, null, null);
                return this;
            }

            public IActivityInputBuilder<TActivity> From<T>(string inputKey, WorkflowValue<T> source) => Set(inputKey, ActivityArgument.From(source));
            public IActivityInputBuilder<TActivity> Value<T>(string inputKey, T? literal) => Set(inputKey, ActivityArgument.Value(literal));

            public IReadOnlyCollection<ArgumentState> Build() => _inputs.Values.OrderBy(input => input.ReferenceKey, StringComparer.Ordinal).ToArray();
        }
    }

    private sealed class ActivityStructureBuilder(
        WorkflowBuilder<TRequest, TResult> owner,
        SequenceBuilder parent,
        string nodeId) : IActivityStructureBuilder
    {
        public string NodeId => nodeId;
        public string ScopeId => parent.ScopeId;

        public ActivityNode Sequence(
            string regionKey,
            Action<ISequenceBuilder> build,
            string? requestedNodeId = null,
            string activityVersionId = "elsa.sequence@1")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(regionKey);
            ArgumentNullException.ThrowIfNull(build);
            ArgumentException.ThrowIfNullOrWhiteSpace(activityVersionId);
            var regionNodeId = owner.AllocateRegionNodeId(nodeId, regionKey, requestedNodeId);
            var regionScopeId = $"{parent.ScopeId}/{nodeId}:{ToStableKey(regionKey)}";
            var region = new SequenceBuilder(owner, regionScopeId, parent);
            build(region);
            return region.Lower(regionNodeId, activityVersionId);
        }
    }
}

internal static class WorkflowIntrinsicAuthoringIds
{
    public const string Set = "elsa.intrinsic.set@1";
    public const string Merge = "elsa.intrinsic.merge@1";
    public const string Reduce = "elsa.intrinsic.reduce@1";
    public const string Return = "elsa.intrinsic.return@1";
    public const string Control = "elsa.intrinsic.control@1";
    public const string SetCorrelationId = "elsa.intrinsic.set-correlation-id@1";
    public const string SetInstanceName = "elsa.intrinsic.set-instance-name@1";
    public const string SetOutput = "elsa.intrinsic.set-output@1";
    public const string Finish = "elsa.intrinsic.finish@1";
}

internal static class WorkflowIntrinsicAuthoringInputKeys
{
    public const string Value = "value";
    public const string Outcome = "outcome";
    public const string Name = "name";
}
