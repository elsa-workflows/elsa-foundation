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
        EfPersistencePreparationResult result;
        try
        {
            var sourceToken = rootConfiguration.GetReloadToken();
            using var snapshot = (ConfigurationRoot)new ConfigurationBuilder()
                .AddInMemoryCollection(rootConfiguration.AsEnumerable())
                .Build();
            if (sourceToken.HasChanged)
                throw new InvalidOperationException("EF persistence preparation refused: configuration-changed");

            result = EfPersistencePreparation.Prepare(context, snapshot);
            if (sourceToken.HasChanged)
                throw new InvalidOperationException("EF persistence preparation refused: configuration-changed");
        }
        catch (InvalidOperationException error) when (error.Message == "EF persistence preparation refused: configuration-changed")
        {
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            // CShells exposes reload exceptions to the management API. Configuration providers
            // and host extensions may include connection values in their exception messages.
            throw new InvalidOperationException("EF persistence preparation refused: configuration-invalid");
        }
        if (result.RefusalCodes.Count != 0)
            throw new InvalidOperationException(
                $"EF persistence preparation refused: {string.Join(", ", result.RefusalCodes)}");

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result.Patch);
    }
}
