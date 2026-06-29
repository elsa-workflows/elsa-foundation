using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Branch coverage for <see cref="RuntimeLoopIterationScopeFactory"/>: the shared per-iteration loop
/// scope primitive (#259) that <c>ForEach</c>/<c>For</c>/<c>While</c>/<c>Do</c> (#264–#267) build on.
/// Proves the current-item/index variables resolve by reference key, that two passes of the same loop
/// get isolated values, and that the iteration scope layers on the loop's enclosing scope chain.
/// </summary>
public sealed class RuntimeLoopIterationScopeFactoryTests
{
    private const string LoopNodeId = "loop-node";
    private const string ItemKey = "var-item";
    private const string ItemName = "currentItem";
    private const string IndexKey = "var-index";
    private const string IndexName = "index";

    private readonly RuntimeLoopIterationScopeFactory _factory = new();

    [Fact]
    public void BuildIterationScope_declares_the_current_item_addressable_by_reference_key()
    {
        var scope = _factory.BuildIterationScope(Request(iterationId: "iter-0", item: "apple", index: 0));

        Assert.Equal(LoopNodeId, scope.ScopeId);
        Assert.Equal("iter-0", scope.ExecutionId);
        Assert.True(scope.TryGetValue(new VariableReference(ItemKey, LoopNodeId), out var value));
        Assert.Equal("apple", value);
    }

    [Fact]
    public void BuildIterationScope_declares_the_iteration_index_when_requested()
    {
        var scope = _factory.BuildIterationScope(Request(iterationId: "iter-3", item: "d", index: 3, withIndex: true));

        Assert.True(scope.TryGetValue(new VariableReference(IndexKey, LoopNodeId), out var index));
        Assert.Equal(3, index);
        Assert.True(scope.TryGetValueByName(IndexName, out var byName));
        Assert.Equal(3, byName);
    }

    [Fact]
    public void BuildIterationScope_omits_the_index_when_no_index_reference_key_is_supplied()
    {
        var scope = _factory.BuildIterationScope(Request(iterationId: "iter-0", item: "x", index: 0));

        Assert.Single(scope.EnumerateVisibleVariables());
        Assert.False(scope.TryGetValueByName(IndexName, out _));
    }

    /// <summary>The Definition-of-Done test: two iterations of a body see distinct item values.</summary>
    [Fact]
    public void Two_iterations_of_the_same_loop_see_distinct_item_values()
    {
        var first = _factory.BuildIterationScope(Request(iterationId: "iter-0", item: "apple", index: 0));
        var second = _factory.BuildIterationScope(Request(iterationId: "iter-1", item: "banana", index: 1));

        // Same authored declaration (same ScopeId + reference key) ...
        Assert.Equal(first.ScopeId, second.ScopeId);
        // ... but distinct per-iteration execution identity, so values never collide.
        Assert.NotEqual(first.ExecutionId, second.ExecutionId);

        var reference = new VariableReference(ItemKey, LoopNodeId);
        Assert.True(first.TryGetValue(reference, out var firstItem));
        Assert.True(second.TryGetValue(reference, out var secondItem));
        Assert.Equal("apple", firstItem);
        Assert.Equal("banana", secondItem);

        // Mutating one pass's item does not leak into the other.
        Assert.True(second.TrySetValue(reference, "cherry"));
        Assert.True(first.TryGetValue(reference, out var firstAfter));
        Assert.Equal("apple", firstAfter);
    }

    [Fact]
    public void BuildIterationScope_layers_the_iteration_scope_on_the_enclosing_chain()
    {
        var enclosing = new VariableScope(
            "container",
            new Dictionary<string, IVariable> { ["var-total"] = new TestVariable("total", "var-total") },
            initialValues: new Dictionary<string, object?> { ["var-total"] = 100 });

        var scope = _factory.BuildIterationScope(Request(iterationId: "iter-0", item: "x", index: 0), parent: enclosing);

        Assert.Same(enclosing, scope.Parent);
        // The body resolves both its iteration item and the enclosing container variable through the chain.
        Assert.True(scope.TryGetValue(new VariableReference(ItemKey, LoopNodeId), out _));
        Assert.True(scope.TryGetValue(new VariableReference("var-total", "container"), out var total));
        Assert.Equal(100, total);
        // Enclosing assignments from the body flow to the owning container scope, not the iteration scope.
        Assert.True(scope.TrySetValue(new VariableReference("var-total", "container"), 200));
        Assert.True(enclosing.TryGetValue(new VariableReference("var-total", "container"), out var updated));
        Assert.Equal(200, updated);
    }

    [Theory]
    [InlineData("", "iter", ItemKey, ItemName)]
    [InlineData(LoopNodeId, "", ItemKey, ItemName)]
    [InlineData(LoopNodeId, "iter", "", ItemName)]
    [InlineData(LoopNodeId, "iter", ItemKey, "")]
    public void BuildIterationScope_rejects_blank_required_values(string owner, string iterationId, string itemKey, string itemName)
    {
        var request = new LoopIterationScopeRequest(owner, iterationId, itemKey, itemName, Item: "x", Index: 0);
        Assert.ThrowsAny<ArgumentException>(() => _factory.BuildIterationScope(request));
    }

    [Fact]
    public void BuildIterationScope_rejects_an_index_sharing_the_item_reference_key()
    {
        var request = new LoopIterationScopeRequest(
            LoopNodeId, "iter-0", ItemKey, ItemName, Item: "x", Index: 0,
            IndexReferenceKey: ItemKey, IndexName: IndexName);

        Assert.Throws<ArgumentException>(() => _factory.BuildIterationScope(request));
    }

    [Fact]
    public void BuildIterationScope_requires_an_index_name_when_an_index_reference_key_is_supplied()
    {
        var request = new LoopIterationScopeRequest(
            LoopNodeId, "iter-0", ItemKey, ItemName, Item: "x", Index: 0,
            IndexReferenceKey: IndexKey, IndexName: null);

        Assert.ThrowsAny<ArgumentException>(() => _factory.BuildIterationScope(request));
    }

    private static LoopIterationScopeRequest Request(string iterationId, object? item, int index, bool withIndex = false) =>
        new(
            OwnerNodeId: LoopNodeId,
            IterationId: iterationId,
            ItemReferenceKey: ItemKey,
            ItemName: ItemName,
            Item: item,
            Index: index,
            IndexReferenceKey: withIndex ? IndexKey : null,
            IndexName: withIndex ? IndexName : null);

    private sealed class TestVariable(string name, string referenceKey) : IVariable
    {
        public string Id { get; set; } = referenceKey;
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; }
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new TestBlock();
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => default;
        public T? Get<T>(IExpressionExecutionContext context) => default;

        private sealed class TestBlock : IMemoryBlock
        {
            public object? Value { get; set; }
            public object? Metadata { get; set; }
        }
    }
}
