using Elsa.Tasks.Core;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// Forces construction of the provider registry during shell startup so duplicate keys fail composition
/// before the first options request.
/// </summary>
public sealed class ValidateActivityInputOptionsProvidersStartupTask(IActivityInputOptionsProviderResolver resolver) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = resolver;
        return Task.CompletedTask;
    }
}
