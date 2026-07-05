using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Liquid.Options;
using Elsa.Expressions.Liquid.Services;
using Fluid;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Encodings.Web;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// #422 (item 3): when a malformed template's <c>{% raw %}</c>-wrapped fallback re-parse of the parse
/// error ALSO failed, <see cref="LiquidTemplateManager"/> cached <c>null</c> and rendering crashed with
/// an unhandled <see cref="NullReferenceException"/>. It must surface a clear "template invalid" error.
/// </summary>
public sealed class LiquidTemplateManagerInvalidTemplateTests
{
    private readonly LiquidTemplateManager _manager = new(
        new(),
        new MemoryCache(new MemoryCacheOptions()),
        new StubEventPublisher(),
        new(HtmlEncoder.Default, [], _ => { }));

    [Fact]
    public async Task RenderAsync_TemplateFailingBothParses_ThrowsDescriptiveError()
    {
        // The parse error for this template embeds template markup, which also breaks the
        // '{% raw %}'-wrapped fallback re-parse — previously caching null and throwing NRE on render.
        const string template = "{% if true %}{% endraw %}{% endif %}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.RenderAsync(template, new StubExpressionExecutionContext()));

        Assert.Contains("Failed to parse Liquid template", exception.Message);
    }

    [Fact]
    public async Task RenderAsync_TemplateWhoseFallbackParses_RendersParseError()
    {
        // Single parse failure keeps the existing behavior: the raw-wrapped parse error is rendered.
        var result = await _manager.RenderAsync("{% assign %}", new StubExpressionExecutionContext());

        Assert.Contains("An identifier was expected after the 'assign' tag", result);
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
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
