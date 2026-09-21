using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Git;

/// <summary>
/// Composition helpers for registering the shared <see cref="IGitClient"/>.
/// </summary>
public static class GitClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IGitClient"/> as a singleton. Consumers that construct their own
    /// <see cref="GitClient"/> with a per-repository executable can skip this and instantiate directly;
    /// this registration serves consumers that resolve the client from the container.
    /// </summary>
    public static IServiceCollection AddGitClient(this IServiceCollection services, Action<GitClientOptions>? configure = null)
    {
        var options = new GitClientOptions();
        configure?.Invoke(options);
        var gitExecutable = string.IsNullOrWhiteSpace(options.GitExecutable) ? "git" : options.GitExecutable;

        services.TryAddSingleton<IGitClient>(sp => new GitClient(gitExecutable, sp.GetRequiredService<ILogger<GitClient>>()));
        return services;
    }
}
