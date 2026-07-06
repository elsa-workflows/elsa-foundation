using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Options;
using Elsa.Expressions.Liquid.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// #422 (item 2): the Liquid template cache used the raw template source as the shared app-wide
/// <see cref="IMemoryCache"/> key with no namespace prefix — unlike the sibling Jint
/// <c>PreparedScriptFactory</c> ("jint:script:" prefix). That risked collisions with any other
/// feature caching by literal string in the same cache. Keys must be namespaced ("liquid:template:").
/// </summary>
public sealed class LiquidTemplateManagerCacheKeyTests
{
    private const string Template = "Hello {{ 1 | plus: 1 }}";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly LiquidTemplateManager _manager;

    public LiquidTemplateManagerCacheKeyTests()
    {
        _manager = new LiquidTemplateManager(
            new(),
            _cache,
            new StubEventPublisher(),
            new(HtmlEncoder.Default, [], _ => { }));
    }

    [Fact]
    public async Task RenderAsync_CachesParsedTemplateUnderNamespacedKey()
    {
        await _manager.RenderAsync(Template, new StubExpressionExecutionContext());

        Assert.True(_cache.TryGetValue("liquid:template:" + Template, out _));
        Assert.False(_cache.TryGetValue(Template, out _)); // never under the bare source key
    }

    [Fact]
    public async Task RenderAsync_ForeignEntryUnderBareKey_DoesNotCollide()
    {
        // Another feature caches an unrelated value under a literal string that happens to equal the
        // template source. Without a namespace prefix, the Liquid manager would retrieve this foreign
        // object as if it were an IFluidTemplate. Namespacing the key sidesteps the collision.
        _cache.Set(Template, new object());

        var result = await _manager.RenderAsync(Template, new StubExpressionExecutionContext());

        Assert.Equal("Hello 2", result);
    }

    private sealed class StubEventPublisher : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubExpressionExecutionContext : IExpressionExecutionContext
    {
        public IMemoryRegister Memory => throw new NotSupportedException();
        public IExpressionExecutionContext? ParentContext { get; set; }
        public CancellationToken CancellationToken => CancellationToken.None;
        public bool IsContainedWithinCompositeActivity() => false;
        public bool TryGetActivityInput(string key, out object? value) => throw new NotSupportedException();
        public bool TryGetWorkflowInput(string key, out object? value) => throw new NotSupportedException();
        public object? GetVariableValueOrDefault(string variableName) => null;
        public string GetCorrelationId() => "";
        public string GetWorkflowDefinitionId() => "";
        public string GetWorkflowDefinitionVersionId() => "";
        public int GetWorkflowDefinitionVersion() => 0;
        public string GetWorkflowInstanceId() => "";
        public object? GetRequiredService(Type type) => throw new NotSupportedException();
        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => throw new NotSupportedException();
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => throw new NotSupportedException();
        public T? Get<T>(IMemoryBlockReference blockReference) => throw new NotSupportedException();
        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null) => throw new NotSupportedException();
        public IVariable? GetVariable(string name, bool localScopeOnly = false) => null;
        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null) => throw new NotSupportedException();
        public IEnumerable<IVariable> EnumerateVariablesInScope() => [];
    }
}
