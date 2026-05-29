namespace Elsa.Mediator.Core.Contracts;

/// <summary>The composed pipeline delegate produced by <see cref="INotificationPipelineBuilder"/>.</summary>
public delegate ValueTask NotificationMiddlewareDelegate(INotificationContext context);
