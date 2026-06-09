using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Extensions;
using Elsa.Mediator.Core.Middleware;

namespace Elsa.Mediator.Commands;

/// <inheritdoc />
public sealed class CommandPipeline : ICommandPipeline
{
    private readonly CommandPipelineBuilder _builder;
    private CommandMiddlewareDelegate _pipeline = null!;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CommandPipeline(IServiceProvider serviceProvider)
    {
        _builder = new(serviceProvider);
        Setup(x => x.UseMiddleware<CommandHandlerInvokerMiddleware>().UseMiddleware<CommandLoggingMiddleware>());
    }

    /// <inheritdoc />
    public CommandMiddlewareDelegate Pipeline => _pipeline;

    /// <inheritdoc />
    public CommandMiddlewareDelegate Setup(Action<ICommandPipelineBuilder>? setup = null)
    {
        setup?.Invoke(_builder);
        _pipeline = _builder.Build();
        return _pipeline;
    }

    /// <inheritdoc />
    public async Task Execute(ICommandContext context) => await Pipeline(context);
}