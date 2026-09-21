namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// The unified dispatch context shared by both the command and request pipelines. A command and a
/// request are mechanically the same operation — resolve the single handler for a
/// <see cref="Message"/> and capture its <see cref="Result"/> — so they flow through one context,
/// one middleware abstraction, and one pipeline. The only per-kind variation is carried explicitly:
/// <see cref="HandlerServiceTypeDefinition"/> selects which handler family to resolve and
/// <see cref="MessageKind"/> labels error messages.
/// </summary>
public interface IMessageContext
{
    /// <summary>The command or request instance being dispatched.</summary>
    object Message { get; }

    /// <summary>The result/response type the handler produces.</summary>
    Type ResultType { get; }

    /// <summary>
    /// The open-generic handler interface to resolve, closed over the message and result types at
    /// dispatch time — e.g. <c>typeof(ICommandHandler&lt;,&gt;)</c> or <c>typeof(IRequestHandler&lt;,&gt;)</c>.
    /// </summary>
    Type HandlerServiceTypeDefinition { get; }

    /// <summary>A short noun ("command"/"request") used in diagnostics and error messages.</summary>
    string MessageKind { get; }

    /// <summary>The service provider to resolve the handler from (the caller's scope).</summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>The cancellation token for the dispatch.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>The handler result, populated by the invoker middleware.</summary>
    object? Result { get; set; }
}
