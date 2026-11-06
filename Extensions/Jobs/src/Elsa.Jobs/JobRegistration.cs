using Elsa.Jobs.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Jobs;

internal static class JobRegistration
{
    internal static IServiceCollection AddServices(IServiceCollection services, Action<JobsOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobRuntimeSettings)))
            throw new InvalidOperationException("Jobs is already configured in this service collection.");
        var options = new JobsOptions();
        configure?.Invoke(options);
        var settings = JobRuntimeSettings.Capture(options);
        services.AddSingleton(settings);
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.TryAddSingleton<JobHandlerRegistry>();
        services.TryAddSingleton<IJobRecurrenceCalculator, JobRecurrenceCalculator>();
        services.TryAddScoped<IJobClient, JobClient>();
        services.AddSingleton<JobsConfigurationValidator>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<JobsConfigurationValidator>());
        services.AddSingleton<IStartupTask>(provider => provider.GetRequiredService<JobsConfigurationValidator>());
        if (settings.EnableWorker)
        {
            if (settings.MaterializeSchedules) services.AddSingleton<JobSchedulePump>();
            services.AddSingleton<JobsWorker>();
            services.AddSingleton<IBackgroundTask>(provider => provider.GetRequiredService<JobsWorker>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<JobsWorker>());
        }
        return services;
    }

    internal static IServiceCollection AddHandler<THandler, TInput, TResult>(IServiceCollection services, JobHandlerSupport support)
        where THandler : class, IJobHandler<TInput, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);
        JobValidation.Support(support);
        if (services.Any(descriptor => descriptor.ImplementationInstance is JobHandlerRegistration registration && registration.Support.Handler == support.Handler))
            throw new JobException("jobs.handler.duplicate", "A handler identity is already registered; semantic identities require one unambiguous contribution.");
        services.TryAddScoped<THandler>();
        services.AddSingleton<JobHandlerRegistration>(new JobHandlerRegistration<THandler, TInput, TResult>(support));
        return services;
    }
}

internal static class JobAuthority
{
    internal static IJobCommands Commands(IServiceProvider services) =>
        RequireOne(services.GetServices<IJobCommands>(), "commands");

    internal static IJobQueries Queries(IServiceProvider services) =>
        RequireOne(services.GetServices<IJobQueries>(), "queries");

    internal static IJobRecurrenceCalculator Recurrence(IServiceProvider services) =>
        RequireOne(services.GetServices<IJobRecurrenceCalculator>(), "recurrence calculator");

    private static T RequireOne<T>(IEnumerable<T> registrations, string kind)
    {
        var values = registrations.Take(2).ToArray();
        return values.Length == 1 ? values[0] : throw new JobException("jobs.authority.invalid",
            $"Jobs requires exactly one configured {kind} authority.");
    }
}

internal sealed class JobsConfigurationValidator(IServiceScopeFactory scopes, JobHandlerRegistry handlers, JobRuntimeSettings settings) : IHostedService, IStartupTask
{
    Task ITask.ExecuteAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopes.CreateAsyncScope();
        _ = JobAuthority.Commands(scope.ServiceProvider);
        _ = JobAuthority.Queries(scope.ServiceProvider);
        _ = JobAuthority.Recurrence(scope.ServiceProvider);
        if (scope.ServiceProvider.GetServices<IJobClient>().Take(2).Count() != 1)
            throw new JobException("jobs.client.duplicate", "Jobs requires exactly one client registration.");
        if (settings.EnableWorker && handlers.Support.Count == 0)
            throw new JobException("jobs.handler.missing", "An enabled Jobs worker requires registered handlers.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
