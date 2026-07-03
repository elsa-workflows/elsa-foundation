using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.Core.Models;

/// <summary>
/// The concrete dispatch context. One shape for commands and requests; the per-kind handler family
/// and error-noun are passed in by the sender. <typeparamref name="TResult"/> is the strongly-typed
/// result the sender reads back after the pipeline completes.
/// </summary>
/// <typeparam name="TResult">The result/response type.</typeparam>
public sealed class MessageContext<TResult> : IMessageContext
    where TResult : notnull
{
    /// <summary>Constructor.</summary>
    public MessageContext(
        object message,
        Type handlerServiceTypeDefinition,
        string messageKind,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        Message = message;
        HandlerServiceTypeDefinition = handlerServiceTypeDefinition;
        MessageKind = messageKind;
        ServiceProvider = serviceProvider;
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc />
    public object Message { get; }

    /// <inheritdoc />
    public Type ResultType => typeof(TResult);

    /// <inheritdoc />
    public Type HandlerServiceTypeDefinition { get; }

    /// <inheritdoc />
    public string MessageKind { get; }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <summary>The strongly-typed result.</summary>
    public TResult? Result { get; set; }

    /// <inheritdoc />
    object? IMessageContext.Result
    {
        get => Result;
        set => Result = (TResult?)value;
    }
}
