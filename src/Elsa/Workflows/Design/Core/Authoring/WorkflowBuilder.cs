using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

public interface IWorkflowBuilder<TRequest, TResult>
{
    WorkflowRequestSource<TRequest> Request { get; }
    ISequenceBuilder Sequence { get; }
    Variable<T> Variable<T>(string name, T? initialValue = default);
    WorkflowRequestMember<T> From<T>(System.Linq.Expressions.Expression<Func<TRequest, T>> member);
    WorkflowValue<T> Value<T>(T? literal);
    ExpressionSource<T> Expression<T>(string language, string source);
    void Set<T>(Variable<T> variable, WorkflowValue<T> value);
    void Return(WorkflowValue<TResult> result);
}

public interface ISequenceBuilder
{
    ActivityCall<TResult> Add<TActivity, TResult>(
        string activityVersionId,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null);
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

public sealed record ActivityNodeHandle(string NodeId);

public sealed record ActivityOutcomeSource(ActivityNodeHandle Node, string OutcomeKey);

public sealed class ActivityCall<TResult>
{
    internal ActivityCall(ActivityNodeHandle node, string scopeId)
    {
        Node = node;
        Result = new ActivityResultSource<TResult>(node.NodeId, "$result", scopeId);
    }

    public ActivityNodeHandle Node { get; }
    public ActivityResultSource<TResult> Result { get; }
    public ActivityResultSource<T> Output<T>(string projectionKey) => new(Node.NodeId, projectionKey, "root");
    public ActivityOutcomeSource Outcome(string outcomeKey) => new(Node, outcomeKey);
}

internal sealed class WorkflowBuilder<TRequest, TResult> : IWorkflowBuilder<TRequest, TResult>, ISequenceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<ActivityNode> _activities = [];
    private readonly List<VariableDefinition> _variables = [];
    private int _nodeOrdinal;

    public WorkflowRequestSource<TRequest> Request { get; } = new();
    public ISequenceBuilder Sequence => this;

    public Variable<T> Variable<T>(string name, T? initialValue = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var referenceKey = ToStableKey(name);
        if (_variables.Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey)))
            throw new InvalidOperationException($"Variable key '{referenceKey}' is already declared in scope 'root'.");

        _variables.Add(new VariableDefinition(
            referenceKey,
            name,
            TypeReferenceFactory.FromClrType(typeof(T), TypeAliasConvention.CanonicalAlias),
            StorageDriverType: null,
            Default: new ArgumentValue(initialValue, AuthoringExpressionTypes.Literal)));
        return new Variable<T>(referenceKey, name, VariableReference.WorkflowScopeId);
    }

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
    {
        ArgumentNullException.ThrowIfNull(variable);
        AddIntrinsic("elsa.intrinsic.set@1", [
            new ArgumentState("variable", new ArgumentValue(
                JsonSerializer.SerializeToElement(new { referenceKey = variable.ReferenceKey, declaringScopeId = VariableReference.WorkflowScopeId }),
                AuthoringExpressionTypes.Variable), null, null, null, null),
            new ArgumentState("value", value.Lower(), null, null, null, null)
        ]);
    }

    public void Return(WorkflowValue<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        AddIntrinsic("elsa.intrinsic.return@1", [new ArgumentState("result", result.Lower(), null, null, null, null)]);
    }

    public ActivityCall<TActivityResult> Add<TActivity, TActivityResult>(
        string activityVersionId,
        Action<IActivityInputBuilder<TActivity>>? inputs = null,
        string? nodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityVersionId);
        var inputBuilder = new ActivityInputBuilder<TActivity>();
        inputs?.Invoke(inputBuilder);
        var ordinal = ++_nodeOrdinal;
        var resolvedNodeId = nodeId ?? $"{ToStableKey(typeof(TActivity).Name)}-{ordinal}";
        if (_activities.Any(activity => StringComparer.Ordinal.Equals(activity.NodeId, resolvedNodeId)))
            throw new InvalidOperationException($"Activity node id '{resolvedNodeId}' is already used.");

        _activities.Add(new ActivityNode(resolvedNodeId, activityVersionId, inputBuilder.Build(), []));
        return new ActivityCall<TActivityResult>(new ActivityNodeHandle(resolvedNodeId), "root");
    }

    internal WorkflowDefinitionState BuildState()
    {
        // Existing authored state mirrors workflow-scope declarations at the document root and in the
        // root structured region. The latter is what today's executable compiler projects into Runtime;
        // T063 replaces this transitional duplication with Runtime-owned variable frames.
        var payload = JsonSerializer.SerializeToElement(new { activities = _activities, variables = _variables }, JsonOptions);
        var root = new ActivityNode(
            "root",
            "elsa.sequence@1",
            Inputs: [],
            Outputs: [],
            new ActivityNodeStructure("elsa.sequence.structure", "1.0.0", payload));
        return new WorkflowDefinitionState(_variables.ToArray(), root, [], [], null, null);
    }

    private void AddIntrinsic(string activityVersionId, IReadOnlyCollection<ArgumentState> inputs)
    {
        _activities.Add(new ActivityNode($"intrinsic-{++_nodeOrdinal}", activityVersionId, inputs, []));
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

    private sealed class ActivityInputBuilder<TActivity> : IActivityInputBuilder<TActivity>
    {
        private readonly Dictionary<string, ArgumentState> _inputs = new(StringComparer.Ordinal);

        public IActivityInputBuilder<TActivity> Set<T>(string inputKey, ActivityArgument<T> argument)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
            if (argument.Kind == ActivityArgumentKind.Omitted)
                return this;
            _inputs[inputKey] = new ArgumentState(inputKey, argument.Lower(inputKey), null, null, null, null);
            return this;
        }

        public IActivityInputBuilder<TActivity> From<T>(string inputKey, WorkflowValue<T> source) => Set(inputKey, ActivityArgument.From(source));
        public IActivityInputBuilder<TActivity> Value<T>(string inputKey, T? literal) => Set(inputKey, ActivityArgument.Value(literal));

        public IReadOnlyCollection<ArgumentState> Build() => _inputs.Values.OrderBy(input => input.ReferenceKey, StringComparer.Ordinal).ToArray();
    }
}
