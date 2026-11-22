using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Secrets.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

internal static class SecretsWriteVersion
{
    public static string For(SecretsDbContext context, Secret secret, string? storedVersion = null)
    {
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(SecretsEfModule.SchemaFamily);
        if (gate is not null && state is null)
            throw new InvalidOperationException("The Secrets family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true })
            throw new EfSchemaFamilyWritesRefusedException(SecretsEfModule.SchemaFamily,
                state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);

        var minimum = secret.Name == secret.Name.ToLowerInvariant() ? "1.0.0" : SecretsEfModule.SchemaVersion;
        if (storedVersion is not null && Version.Parse(storedVersion) > Version.Parse(minimum))
            minimum = storedVersion;
        // Ungated operator projection repair retains the row's format; it cannot finalize a family.
        var version = state?.WriteVersion ?? storedVersion ?? SecretsEfModule.SchemaVersion;
        if (Version.Parse(version) < Version.Parse(minimum))
            throw new EfSchemaWriteRefusedException(SecretsEfModule.SchemaFamily, version, minimum);
        return version;
    }
}
