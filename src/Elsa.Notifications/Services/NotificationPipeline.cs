using Elsa.Notifications.Core;
using Elsa.Notifications.Helpers;
using Elsa.Notifications.Middleware;

namespace Elsa.Notifications.Services
{
    /// <inheritdoc />
    public sealed class NotificationPipeline : INotificationPipeline
    {
        private readonly IServiceProvider _serviceProvider;
        private NotificationMiddlewareDelegate? _pipeline;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationPipeline"/> class.
        /// </summary>
        public NotificationPipeline(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

        /// <inheritdoc />
        public NotificationMiddlewareDelegate Pipeline => _pipeline ??= CreateDefaultPipeline();

        /// <inheritdoc />
        public NotificationMiddlewareDelegate Setup(Action<INotificationPipelineBuilder>? setup = default)
        {
            var builder = new NotificationPipelineBuilder(_serviceProvider);
            setup?.Invoke(builder);
            _pipeline = builder.Build();
            return _pipeline;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(INotificationContext context) => await Pipeline(context);

        private NotificationMiddlewareDelegate CreateDefaultPipeline() => Setup(x =>
        {
            x.UseMiddleware<NotificationHandlerInvokerMiddleware>();
        });
    }
}
