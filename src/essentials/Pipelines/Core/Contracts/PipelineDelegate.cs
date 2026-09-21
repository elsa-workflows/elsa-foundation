namespace Elsa.Pipelines.Core.Contracts;

/// <summary>
/// The composed pipeline delegate for a given context type. One canonical delegate shape shared by
/// every per-message pipeline (mediator commands/requests, events) so a single generic
/// <see cref="IPipelineBuilder{TContext}"/> can compose any of them.
/// </summary>
/// <typeparam name="TContext">The message context flowing through the pipeline.</typeparam>
public delegate ValueTask PipelineDelegate<in TContext>(TContext context);
