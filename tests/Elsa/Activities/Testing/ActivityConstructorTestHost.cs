using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Testing;

/// <summary>
/// Populates the activity-constructor registry for test hosts that build a service provider
/// directly instead of using the shell startup-task runner.
/// </summary>
public static class ActivityConstructorTestHost
{
    /// <summary>
    /// Initializes the constructor registry once. The registration check makes this safe for the
    /// execution harness when a test has already run all startup tasks explicitly.
    /// </summary>
    public static Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = services.GetRequiredService<IActivityConstructorRegistry>();
        var constructors = services.GetServices<IActivityConstructor>().ToArray();
        if (constructors.All(constructor => IsRegistered(registry, constructor)))
            return Task.CompletedTask;

        // The production startup task obtains this same constructor set through its initialization
        // event before calling AddAll. Direct test hosts intentionally omit the event/task shell,
        // so this helper performs only the final registry population step.
        registry.AddAll(constructors);
        return Task.CompletedTask;
    }

    private static bool IsRegistered(IActivityConstructorRegistry registry, IActivityConstructor constructor)
    {
        foreach (var schemaVersion in constructor.SupportedSchemaVersions)
        {
            try
            {
                if (registry.Resolve(constructor.ConsumerKey, schemaVersion).GetType() != constructor.GetType())
                    return false;
            }
            catch (ActivityResolutionException)
            {
                return false;
            }
        }

        return true;
    }
}
