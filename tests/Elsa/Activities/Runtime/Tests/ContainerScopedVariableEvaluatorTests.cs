using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Runtime read path for container-scoped variables (#207). A descendant activity resolves a
/// structured <see cref="VariableReference"/> to a variable declared by a visible ancestor
/// container scope, with nearest-scope shadowing — proven at the expression-evaluation seam.
/// </summary>
public sealed class ContainerScopedVariableEvaluatorTests
{
    private const string SequenceScopeId = "seq-1";

    private static ExpressionEvaluator Evaluator() =>
        new(
            new ExpressionDescriptorRegistry([new DefaultExpressionDescriptorProvider()]),
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(ExpressionEvaluatorOptions.Empty));

    [Fact]
    public async Task Descendant_resolves_container_scoped_variable_by_structured_reference()
    {
        var scope = ContainerScope(("var-counter", "Counter", 7));
        var context = new ScopedExpressionContext(scope);

        var result = await Evaluator().EvaluateAsync<int>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, new VariableReference("var-counter", SequenceScopeId)),
            context);

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task Workflow_scoped_reference_resolves_through_outer_scope_from_within_container()
    {
        var scope = ContainerScope(("var-counter", "Counter", 7));
        var context = new ScopedExpressionContext(scope);

        var result = await Evaluator().EvaluateAsync<string>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, new VariableReference("var-greeting", VariableReference.WorkflowScopeId)),
            context);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Reference_to_invisible_scope_resolves_to_null()
    {
        var scope = ContainerScope(("var-counter", "Counter", 7));
        var context = new ScopedExpressionContext(scope);

        var result = await Evaluator().EvaluateAsync<int?>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, new VariableReference("var-counter", "some-other-container")),
            context);

        Assert.Null(result);
    }

    [Fact]
    public async Task Nested_scopes_resolve_each_declaration_explicitly_by_structured_reference()
    {
        var workflowScope = new VariableScope(VariableReference.WorkflowScopeId, ByKey(("var-greeting", "Greeting", "hello")));
        var outerScope = new VariableScope("outer", ByKey(("var-outer", "OuterCount", 1)), workflowScope);
        var innerScope = new VariableScope("inner", ByKey(("var-inner", "InnerCount", 2)), outerScope);
        var context = new ScopedExpressionContext(innerScope);

        var inner = await Evaluator().EvaluateAsync<int>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, new VariableReference("var-inner", "inner")), context);
        var outer = await Evaluator().EvaluateAsync<int>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, new VariableReference("var-outer", "outer")), context);

        Assert.Equal(2, inner);
        Assert.Equal(1, outer);
    }

    [Fact]
    public void Bare_name_lookup_resolves_nearest_scope_allowing_intentional_shadowing()
    {
        // Outer and inner containers both declare a variable named "Counter" in their own scope.
        // Nearest-scope (inner) wins for bare-name resolution; the outer one is shadowed.
        var outerScope = new VariableScope("outer", ByKey(("var-outer", "Counter", 1)));
        var innerScope = new VariableScope("inner", ByKey(("var-inner", "Counter", 2)), outerScope);

        var nearest = innerScope.ResolveByName("Counter");

        Assert.NotNull(nearest);
        Assert.Equal(2, nearest!.DefaultValue);
        Assert.True(innerScope.TryResolve(new VariableReference("var-inner", "inner"), out var resolvedInner));
        Assert.Same(nearest, resolvedInner);
    }

    [Fact]
    public async Task Descendant_assignment_to_visible_container_variable_is_observed_by_a_later_read()
    {
        var scope = ContainerScope(("var-counter", "Counter", 0));
        var context = new ScopedExpressionContext(scope);
        var reference = new VariableReference("var-counter", SequenceScopeId);

        Assert.True(context.TrySetScopedVariableValue(reference, 42));

        var result = await Evaluator().EvaluateAsync<int>(
            new TestExpression(WellKnownExpressionDescriptorTypes.Variable, reference), context);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Assignment_to_an_invisible_scope_is_rejected_by_the_runtime_guard()
    {
        var scope = ContainerScope(("var-counter", "Counter", 0));
        var context = new ScopedExpressionContext(scope);

        var assigned = context.TrySetScopedVariableValue(new VariableReference("var-counter", "sibling-container"), 99);

        Assert.False(assigned);
    }

    [Fact]
    public void Sibling_branches_in_one_container_execution_share_assigned_values()
    {
        // Two sibling branch activities resolve the same container scope instance, so an assignment
        // made by one branch is observed by the other (the container owns the shared running state).
        var container = ContainerScope(("var-shared", "Shared", "initial"));
        var branchA = new ScopedExpressionContext(container);
        var branchB = new ScopedExpressionContext(container);
        var reference = new VariableReference("var-shared", SequenceScopeId);

        Assert.True(branchA.TrySetScopedVariableValue(reference, "from-branch-a"));

        Assert.True(branchB.TryGetScopedVariableValue(reference, out var observed));
        Assert.Equal("from-branch-a", observed);
    }

    private static VariableScope ContainerScope(params (string ReferenceKey, string Name, object? Value)[] containerVariables)
    {
        var workflowScope = new VariableScope(VariableReference.WorkflowScopeId, ByKey(("var-greeting", "Greeting", "hello")));
        return new VariableScope(SequenceScopeId, ByKey(containerVariables), workflowScope);
    }

    private static IReadOnlyDictionary<string, IVariable> ByKey(params (string ReferenceKey, string Name, object? Value)[] variables) =>
        variables.ToDictionary(
            v => v.ReferenceKey,
            v => (IVariable)new Variable(v.Name, v.Value),
            StringComparer.Ordinal);

    private sealed class ScopedExpressionContext(VariableScope scope) : IExpressionExecutionContext, IScopedVariableProvider
    {
        public IMemoryRegister Memory { get; } = new ScopedMemoryRegister();
        public IExpressionExecutionContext? ParentContext { get; set; }
        public CancellationToken CancellationToken => CancellationToken.None;

        public bool TryGetScopedVariableValue(VariableReference reference, out object? value) =>
            scope.TryGetValue(reference, out value);

        public bool TrySetScopedVariableValue(VariableReference reference, object? value) =>
            scope.TrySetValue(reference, value);

        public bool IsContainedWithinCompositeActivity() => false;
        public bool TryGetActivityInput(string key, out object? value) { value = null; return false; }
        public bool TryGetWorkflowInput(string key, out object? value) { value = null; return false; }
        public object? GetVariableValueOrDefault(string variableName) => GetVariable(variableName)?.Get(this);
        public string GetCorrelationId() => string.Empty;
        public string GetWorkfowDefinitionId() => string.Empty;
        public string GetWorkfowDefinitionVersionId() => string.Empty;
        public int GetWorkfowDefinitionVersion() => 0;
        public string GetWorkfowInstanceId() => string.Empty;
        public object? GetRequiredService(Type type) => throw new InvalidOperationException($"No service registered for '{type}'.");
        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => Memory.Declare(blockReference);
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => Memory.TryGetBlock(blockReference.Id, out block);
        public T? Get<T>(IMemoryBlockReference blockReference) => (T?)GetBlock(blockReference).Value;

        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null)
        {
            var block = Memory.Declare(blockReference);
            block.Value = value;
            configure?.Invoke(block);
        }

        // Workflow-scope lookups (no structured scope) walk the chain by name as a convenience.
        public IVariable? GetVariable(string name, bool localScopeOnly = false) => scope.ResolveByName(name);

        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null)
        {
            var variable = new Variable<T>(name, value!);
            Set(variable, value, configure);
            return variable;
        }

        public IEnumerable<IVariable> EnumerateVariablesInScope() => [];
    }

    private sealed class ScopedMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>(StringComparer.Ordinal);
    }

    private sealed class TestExpression(string type, object? value) : IExpression
    {
        public string Type { get; set; } = type;
        public object? Value { get; set; } = value;
        public TValue GetValue<TValue>() => (TValue)Value!;
    }
}
