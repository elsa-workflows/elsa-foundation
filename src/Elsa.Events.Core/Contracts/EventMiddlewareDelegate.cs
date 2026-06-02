namespace Elsa.Events.Core.Contracts;

/// <summary>The composed pipeline delegate produced by <see cref="IEventPipelineBuilder"/>.</summary>
public delegate ValueTask EventMiddlewareDelegate(IEventContext context);
