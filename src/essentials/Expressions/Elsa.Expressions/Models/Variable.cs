using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Models;

/// <summary>
/// Describes a variable declaration. Runtime values are owned by variable frames/scopes.
/// </summary>
public class Variable : IVariable
{
    /// <inheritdoc />
    public Variable()
    {
    }

    /// <inheritdoc />
    public Variable(string name)
    {
        Id = GetIdFromName(name);
        Name = name;
    }

    /// <inheritdoc />
    public Variable(string name, object? value = null) : this(name)
    {
        DefaultValue = value;
    }

    public Variable(string name, object? value = null, string? id = null) : this(name, value)
    {
        if (id != null)
        {
            Id = id;
        }
    }

    /// <summary>
    /// The name of the variable.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The stable key of the declaration within its declaring scope.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// A default value for the variable.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// The storage driver type to use for persistence.
    /// If no driver is specified, the owning runtime frame uses its configured persistence policy.
    /// </summary>
    public Type? StorageDriverType { get; set; }

    private static string GetIdFromName(string? name) => $"{name ?? "Unnamed"}{nameof(Variable)}";
}

/// <summary>
/// Describes a strongly typed variable declaration.
/// </summary>
/// <typeparam name="T">The type of the variable.</typeparam>
public sealed class Variable<T> : Variable, IVariable<T>
{
    /// <inheritdoc />
    public Variable()
    {
    }

    /// <inheritdoc />
    [Obsolete("Use the constructor that takes a name parameter instead.", true)]
    public Variable(T value)
    {
        DefaultValue = value;
    }

    /// <inheritdoc />
    public Variable(string name, T value) : base(name, value)
    {
    }

    public Variable(string name, T value, string? id = null) : base(name, value, id)
    {
    }

    /// <summary>
    /// Sets the <see cref="Variable.StorageDriverType"/> to the specified type.
    /// </summary>
    public Variable<T> WithStorageDriver(Type type)
    {
        StorageDriverType = type;
        return this;
    }

    public Variable<T> WithId(string id)
    {
        Id = id;
        return this;
    }

    public Variable<T> WithName(string name)
    {
        Name = name;
        return this;
    }

    public Variable<T> WithValue(T value)
    {
        DefaultValue = value;
        return this;
    }
}
