using System.Text.Json.Serialization;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// The variable a <see cref="VariableReference{T}"/> or <see cref="ReadOnlyVariableReference{T}"/> input is bound to
/// (spec 1231 FR-022): the workflow author binds the input to a declared variable, the publisher resolves it to its
/// declaring scope and key, and the runtime hands the activity the pinned target. It carries no value: an activity reads
/// and writes the variable only through <see cref="Contracts.IStepVariables"/>, inside its own step.
/// </summary>
public interface IVariableReference
{
    /// <summary>The variable's declared name.</summary>
    string Name { get; }

    /// <summary>The declaring scope the publisher resolved the binding to (<c>workflow</c> for a workflow variable).</summary>
    string ScopeId { get; }

    /// <summary>The variable's reference key within its declaring scope.</summary>
    string VariableKey { get; }

    /// <summary>Whether the activity may write the variable: false for a <see cref="ReadOnlyVariableReference{T}"/>.</summary>
    bool IsWritable { get; }

    /// <summary>The value type the activity reads and writes the variable as.</summary>
    Type ValueType { get; }
}

/// <summary>
/// An activity input the workflow author binds to a declared variable, read and written through
/// <c>context.Variables.Get</c> and <c>context.Variables.Set</c> (ADR 0090 decision 4). A write is an intent committed
/// with the step; changing the value <c>Get</c> returned, without <c>Set</c>, writes nothing (C2).
/// </summary>
public sealed class VariableReference<T> : IVariableReference
{
    [JsonConstructor]
    public VariableReference(string name, string scopeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        Name = name;
        ScopeId = scopeId;
        VariableKey = variableKey;
    }

    public string Name { get; }
    public string ScopeId { get; }
    public string VariableKey { get; }

    [JsonIgnore]
    public bool IsWritable => true;

    [JsonIgnore]
    public Type ValueType => typeof(T);

    public override string ToString() => Name;
}

/// <summary>
/// An activity input the workflow author binds to a declared variable that the activity only reads, through
/// <c>context.Variables.Get</c>. Setting it throws.
/// </summary>
public sealed class ReadOnlyVariableReference<T> : IVariableReference
{
    [JsonConstructor]
    public ReadOnlyVariableReference(string name, string scopeId, string variableKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        Name = name;
        ScopeId = scopeId;
        VariableKey = variableKey;
    }

    public string Name { get; }
    public string ScopeId { get; }
    public string VariableKey { get; }

    [JsonIgnore]
    public bool IsWritable => false;

    [JsonIgnore]
    public Type ValueType => typeof(T);

    public override string ToString() => Name;
}

/// <summary>Whether an activity input bound to a variable lets the activity write it (spec 1231 FR-022).</summary>
public enum ActivityVariableAccess
{
    /// <summary>A <see cref="ReadOnlyVariableReference{T}"/>: the activity reads the variable.</summary>
    Read,

    /// <summary>A <see cref="VariableReference{T}"/>: the activity reads and writes the variable.</summary>
    ReadWrite
}

/// <summary>
/// The variable-reference facts of an activity input (spec 1231 FR-022): its direction and the value type the activity
/// reads the variable as. The input's own type is <see cref="VariableReferenceTypes.Alias"/>.
/// </summary>
public sealed record ActivityInputVariableReference(
    ActivityVariableAccess Access,
    Elsa.Primitives.Models.ValueTypeDescriptor ValueType);

/// <summary>Recognises the variable-reference input types, by CLR type or, for a reflection-only scan, by name.</summary>
public static class VariableReferenceTypes
{
    /// <summary>The portable type of a variable-reference input's pinned binding: the reference, never a value.</summary>
    public const string Alias = "Elsa.VariableReference";

    /// <summary>The full name of <see cref="VariableReference{T}"/>'s generic definition.</summary>
    public static string WritableDefinitionFullName { get; } = typeof(VariableReference<>).FullName!;

    /// <summary>The full name of <see cref="ReadOnlyVariableReference{T}"/>'s generic definition.</summary>
    public static string ReadOnlyDefinitionFullName { get; } = typeof(ReadOnlyVariableReference<>).FullName!;

    /// <summary>
    /// Whether <paramref name="type"/> is a variable-reference input type, and if so its direction and value type. Compares
    /// the generic definition by name, so it also works for a type loaded for reflection only.
    /// </summary>
    public static bool TryGet(Type type, out ActivityVariableAccess access, out Type valueType)
    {
        ArgumentNullException.ThrowIfNull(type);
        access = default;
        valueType = typeof(object);
        if (!type.IsGenericType || type.IsGenericTypeDefinition)
            return false;

        var definition = type.GetGenericTypeDefinition().FullName;
        if (StringComparer.Ordinal.Equals(definition, WritableDefinitionFullName))
            access = ActivityVariableAccess.ReadWrite;
        else if (StringComparer.Ordinal.Equals(definition, ReadOnlyDefinitionFullName))
            access = ActivityVariableAccess.Read;
        else
            return false;

        valueType = type.GetGenericArguments()[0];
        return true;
    }
}
