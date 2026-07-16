using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Authoring;

/// <summary>
/// A typed authoring-time source. It is lowered immediately to authored argument state and is never
/// stored in a workflow executable or used as runtime value storage.
/// </summary>
public abstract class WorkflowValue<T>
{
    internal abstract ArgumentValue Lower();
}

public sealed class WorkflowRequestMember<T> : WorkflowValue<T>
{
    internal WorkflowRequestMember(string memberKey, string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberKey);
        MemberKey = memberKey;
        Path = path;
    }

    public string MemberKey { get; }
    public string? Path { get; }

    internal override ArgumentValue Lower() => new(
        JsonSerializer.SerializeToElement(new { memberKey = MemberKey, path = Path }),
        AuthoringExpressionTypes.WorkflowRequest);
}

public sealed class VariableRead<T> : WorkflowValue<T>
{
    internal VariableRead(string referenceKey, string declaringScopeId)
    {
        ReferenceKey = referenceKey;
        DeclaringScopeId = declaringScopeId;
    }

    public string ReferenceKey { get; }
    public string DeclaringScopeId { get; }

    internal override ArgumentValue Lower() => new(
        JsonSerializer.SerializeToElement(new { referenceKey = ReferenceKey, declaringScopeId = DeclaringScopeId }),
        AuthoringExpressionTypes.Variable);
}

public sealed class ActivityResultSource<T> : WorkflowValue<T>
{
    internal ActivityResultSource(string producerNodeId, string projectionKey, string producerScopeId)
    {
        ProducerNodeId = producerNodeId;
        ProjectionKey = projectionKey;
        ProducerScopeId = producerScopeId;
    }

    public string ProducerNodeId { get; }
    public string ProjectionKey { get; }
    public string ProducerScopeId { get; }

    internal override ArgumentValue Lower() => new(
        JsonSerializer.SerializeToElement(new
        {
            producerNodeId = ProducerNodeId,
            projectionKey = ProjectionKey,
            producerScopeId = ProducerScopeId
        }),
        AuthoringExpressionTypes.ActivityResult);
}

public sealed class ExpressionSource<T> : WorkflowValue<T>
{
    internal ExpressionSource(string language, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Language = language;
        Source = source;
    }

    public string Language { get; }
    public string Source { get; }

    internal override ArgumentValue Lower() => new(Source, Language);
}

internal sealed class LiteralWorkflowValue<T>(T? value) : WorkflowValue<T>
{
    internal override ArgumentValue Lower() => new(value, AuthoringExpressionTypes.Literal);
}

internal static class AuthoringExpressionTypes
{
    public const string Literal = "Literal";
    public const string Variable = "Variable";
    public const string WorkflowRequest = "WorkflowRequest";
    public const string ActivityResult = "ActivityResult";
    public const string Default = "Default";
}
