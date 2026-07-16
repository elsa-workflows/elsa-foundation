using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

public enum ActivityArgumentKind
{
    Omitted,
    Literal,
    Source,
    Default
}

/// <summary>
/// Generated-method argument carrier. The carrier exists only while building authored state.
/// </summary>
public readonly struct ActivityArgument<T>
{
    private readonly bool _isExplicitNull;

    private ActivityArgument(ActivityArgumentKind kind, T? literal, WorkflowValue<T>? source, bool isExplicitNull = false)
    {
        Kind = kind;
        Literal = literal;
        Source = source;
        _isExplicitNull = isExplicitNull;
    }

    public ActivityArgumentKind Kind { get; }
    public T? Literal { get; }
    public WorkflowValue<T>? Source { get; }

    public static ActivityArgument<T> Value(T? value) => new(ActivityArgumentKind.Literal, value, null);
    public static ActivityArgument<T> From(WorkflowValue<T> source) => new(ActivityArgumentKind.Source, default, source ?? throw new ArgumentNullException(nameof(source)));
    public static ActivityArgument<T> Null() => new(ActivityArgumentKind.Literal, default, null, isExplicitNull: true);
    public static ActivityArgument<T> Default() => new(ActivityArgumentKind.Default, default, null);

    public static implicit operator ActivityArgument<T>(T? value) => Value(value);
    public static implicit operator ActivityArgument<T>(WorkflowValue<T> source) => From(source);

    internal ArgumentValue Lower(string inputKey) => Kind switch
    {
        ActivityArgumentKind.Literal => new ArgumentValue(_isExplicitNull ? null : Literal, AuthoringExpressionTypes.Literal),
        ActivityArgumentKind.Source => Source!.Lower(),
        ActivityArgumentKind.Default => new ArgumentValue(null, AuthoringExpressionTypes.Default),
        _ => throw new InvalidOperationException($"Input '{inputKey}' was omitted and cannot be lowered without its published contract default.")
    };
}

public static class ActivityArgument
{
    public static ActivityArgument<T> Value<T>(T? value) => ActivityArgument<T>.Value(value);
    public static ActivityArgument<T> From<T>(WorkflowValue<T> source) => ActivityArgument<T>.From(source);
    public static ActivityArgument<T> Null<T>() => ActivityArgument<T>.Null();
    public static ActivityArgument<T> Default<T>() => ActivityArgument<T>.Default();
}
