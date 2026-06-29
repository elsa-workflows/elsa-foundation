using Elsa.Expressions.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Minimal <see cref="IVariable"/> for a runtime scope declaration, shared by the container-scope and
/// loop-iteration scope factories (ADR 0027, #259). Carries the declaration's name, reference key (as
/// the memory block id), and default value without depending on the activity authoring stack. The
/// <see cref="IVariable"/> value accessors are vestigial: runtime scope resolution reads values from the
/// owning <c>VariableScope</c>'s value store, not from the variable, so they only echo the default.
/// </summary>
internal sealed class RuntimeScopedVariableModel(string name, string referenceKey, object? defaultValue = null) : IVariable
{
    public string Id { get; set; } = referenceKey;
    public string Name { get; set; } = name;
    public object? DefaultValue { get; set; } = defaultValue;
    public Type? StorageDriverType { get; set; }

    public IMemoryBlock Declare() => new RuntimeScopedVariableBlock(DefaultValue);

    public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
        DefaultValue is T typed ? typed : default;

    public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T typed ? typed : default;

    private sealed class RuntimeScopedVariableBlock(object? value) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; }
    }
}
