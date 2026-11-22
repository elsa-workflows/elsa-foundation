using CShells.Lifecycle;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Options;
using Elsa.Secrets.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Secrets.Services;

/// <summary>
/// Fails host and shell startup when <see cref="SecretsOptions.DefaultWritableStoreName"/> names a
/// store that is not registered or is not writable, instead of waiting for the first owned-secret or
/// OAuth grant write.
/// </summary>
public sealed class DefaultWritableSecretStoreValidator(
    IOptions<SecretsOptions> options,
    ISecretStoreRegistry stores) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Validate() =>
        DefaultWritableSecretStore.GetRequiredWritable(stores, options.Value.DefaultWritableStoreName);
}
