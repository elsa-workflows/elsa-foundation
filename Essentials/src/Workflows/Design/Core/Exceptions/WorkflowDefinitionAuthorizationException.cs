namespace Elsa.Workflows.Design.Core.Exceptions;

/// <summary>An owning workflow authorization operation could not establish a decision.</summary>
/// <remarks>Infrastructure details remain in <see cref="Exception.InnerException"/>, never in the outer message.</remarks>
public sealed class WorkflowDefinitionAuthorizationException : Exception
{
    /// <summary>Preserves a failed owner authorization operation without including its private cause in the message.</summary>
    /// <param name="definitionId">The definition whose current authority could not be established.</param>
    /// <param name="operation">The requested owner operation, such as read or manage.</param>
    /// <param name="innerException">The original infrastructure or domain failure.</param>
    public WorkflowDefinitionAuthorizationException(string definitionId, string operation, Exception innerException)
        : base("Workflow definition authorization could not be completed.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(innerException);
        DefinitionId = definitionId;
        Operation = operation;
    }

    /// <summary>The definition whose current authority could not be established.</summary>
    public string DefinitionId { get; }

    /// <summary>The requested owner operation.</summary>
    public string Operation { get; }
}
