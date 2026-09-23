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

        var result = EfPersistencePreparation.Prepare(context, rootConfiguration);
        if (result.RefusalCodes.Count != 0)
            throw new InvalidOperationException(
                $"EF persistence preparation refused: {string.Join(", ", result.RefusalCodes)}");

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result.Patch);
    }
}
