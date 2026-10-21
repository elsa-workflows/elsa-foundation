using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// The Secrets model as builds from before <c>FreeNameOnDelete</c> had it: keyed by tenant and name alone, without the
/// tombstone that lets a deleted secret free its name. A context built with it writes the way a host still on such a build
/// does into a database the migration has already been applied to: a delete updates the row in place.
/// </summary>
internal class PreReuseSecretsModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var secret = modelBuilder.Entity<SecretRecord>();
        secret.HasKey(record => new { record.TenantId, record.NormalizedName });
        secret.Ignore(record => record.Tombstone);
    }
}
