using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;

namespace Elsa.Modularity.EntityFramework;

/// <summary>Applies resolved EF targets to a fresh shell before feature construction.</summary>
public sealed class EfPersistenceShellSettingsPreparer(IConfiguration rootConfiguration) : IShellSettingsPreparer
{
    public Task<ShellSettingsPreparationResult> PrepareAsync(
        ShellSettingsPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        // A shell generation must resolve every field against one view of the host sources.
        // Reading the live IConfiguration throughout resolution could combine values from
        // opposite sides of a file reload (for example, a new resource definition with an
        // old named connection). Capture the composed view for this invocation instead.
        var sourceToken = rootConfiguration.GetReloadToken();
        using var snapshot = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(rootConfiguration.AsEnumerable())
            .Build();
        if (sourceToken.HasChanged)
            throw new InvalidOperationException("EF persistence preparation refused: configuration-changed");

        var result = EfPersistencePreparation.Prepare(context, snapshot);
        if (sourceToken.HasChanged)
            throw new InvalidOperationException("EF persistence preparation refused: configuration-changed");
        if (result.RefusalCodes.Count != 0)
            throw new InvalidOperationException(
                $"EF persistence preparation refused: {string.Join(", ", result.RefusalCodes)}");

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result.Patch);
    }
}
