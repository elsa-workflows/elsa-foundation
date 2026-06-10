using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class SimpleActivityExecutionContext(
    IServiceProvider serviceProvider,
    IActivity activity,
    CancellationToken cancellationToken)
    : IActivityExecutionContext, IExpressionExecutionContext
{
    private readonly IMemoryRegister _memory = new SimpleMemoryRegister();
    private readonly List<string> _outcomes = [];

    public IExpressionExecutionContext ExpressionExecutionContext => this;
    public IActivity Activity { get; } = activity;
    public IActivityExecutionContext ParentActivityExecutionContext => null!;
    public IMemoryRegister Memory => _memory;
    public IExpressionExecutionContext? ParentContext { get; set; }
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public TService GetRequiredService<TService>() where TService : notnull =>
        (TService)GetRequiredService(typeof(TService))!;

    public object? GetRequiredService(Type type) => serviceProvider.GetService(type)
        ?? throw new InvalidOperationException($"Required service '{type.FullName}' is not registered.");

    public T? Get<T>(InputArgument<T>? input)
    {
        if (input is null)
            return default;

        return Get<T>(input.MemoryBlockReference());
    }

    public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null)
    {
        if (output is null)
            return;

        Set(output.MemoryBlockReference(), value);
    }

    public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => AsyncEnumerable.Empty<ActivityOutputs>();

    public void SetOutcomes(string[] outcomes)
    {
        _outcomes.Clear();
        _outcomes.AddRange(outcomes);
    }

    public IEnumerable<string> GetOutcomes() => _outcomes;

    public bool IsContainedWithinCompositeActivity() => false;
    public bool TryGetActivityInput(string key, out object? value) => TryGetById(key, out value);
    public bool TryGetWorkflowInput(string key, out object? value) => TryGetById(key, out value);
    public object? GetVariableValueOrDefault(string variableName) => null;
    public string GetCorrelationId() => string.Empty;
    public string GetWorkfowDefinitionId() => string.Empty;
    public string GetWorkfowDefinitionVersionId() => string.Empty;
    public int GetWorkfowDefinitionVersion() => 0;
    public string GetWorkfowInstanceId() => string.Empty;

    public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => _memory.Declare(blockReference);
    public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => _memory.TryGetBlock(blockReference.Id, out block);
    public T? Get<T>(IMemoryBlockReference blockReference) => ConvertValue<T>(GetBlock(blockReference).Value);

    public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null)
    {
        var block = _memory.Declare(blockReference);
        block.Value = value;
        configure?.Invoke(block);
    }

    public IVariable? GetVariable(string name, bool localScopeOnly = false) => null;

    public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null)
    {
        var variable = new SimpleVariable(name, value);
        Set(variable, value, configure);
        return variable;
    }

    public IEnumerable<IVariable> EnumerateVariablesInScope() => [];

    private bool TryGetById(string key, out object? value)
    {
        if (_memory.Blocks.TryGetValue(key, out var block))
        {
            value = block.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static T? ConvertValue<T>(object? value)
    {
        if (value is null)
            return default;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (value is JsonElement json)
            return json.Deserialize<T>();

        if (targetType.IsEnum)
            return (T)Enum.Parse(targetType, value.ToString()!, ignoreCase: true);

        return (T?)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private sealed class SimpleMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>(StringComparer.Ordinal);
    }

    private sealed class SimpleMemoryBlock(object? value = null, object? metadata = null) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; } = metadata;
    }

    private class SimpleMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public virtual IMemoryBlock Declare() => new SimpleMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            ConvertValue<T>(GetValue(memoryRegister));

        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);

        private object? GetValue(IMemoryRegister memoryRegister)
        {
            if (!memoryRegister.Blocks.TryGetValue(Id, out var block))
                block = memoryRegister.Declare(this);

            return block.Value;
        }
    }

    private sealed class SimpleVariable(string name, object? defaultValue = null) : SimpleMemoryBlockReference(name), IVariable
    {
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public override IMemoryBlock Declare() => new SimpleMemoryBlock(DefaultValue);
    }
}
