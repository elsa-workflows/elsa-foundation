using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The only service <see cref="EfConnectionDefaults.ResolveConnectionString"/> asks for is
/// <see cref="IConfiguration"/>, so the suites that exercise it hand it this instead of building a container.
/// A null configuration stands for a host that registered none.
/// </summary>
internal sealed class ConfigurationServices(IConfiguration? configuration) : IServiceProvider
{
    public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
}
