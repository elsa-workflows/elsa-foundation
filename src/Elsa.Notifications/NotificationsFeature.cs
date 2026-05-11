using CShells.Features;
using Elsa.Notifications.Core;
using Elsa.Notifications.Enums;
using Elsa.Notifications.Services;
using Elsa.Notifications.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Notifications
{
    /// <summary>
    /// Configures the Notifications feature, which provides services to enable notifications in workflows.
    /// </summary>
    [ShellFeature(
        name: "Notifications",
        DisplayName = "Notifications",
        Description = "Provides services to enable notifications"
    )]
    public class NotificationsFeature : IShellFeature
    {
        public NotificationStrategy DefaultPublishingStrategy { get; set; } = NotificationStrategy.Sequential;

        public void ConfigureServices(IServiceCollection services)
        {

            RegisterDefaultEventPublishingStrategy(services);
            services.AddSingleton<INotificationPipeline, NotificationPipeline>();
        }

        void RegisterDefaultEventPublishingStrategy(IServiceCollection services)
        {
            switch (DefaultPublishingStrategy)
            {
                case NotificationStrategy.Background:
                    services.AddSingleton<IEventPublishingStrategy, BackgroundProcessingStrategy>();
                    break;
                case NotificationStrategy.Parallel:
                    services.AddSingleton<IEventPublishingStrategy, ParallelProcessingStrategy>();
                    break;
                default:
                    services.AddSingleton<IEventPublishingStrategy, SequentialProcessingStrategy>();
                    break;
            }
        }
    }
}