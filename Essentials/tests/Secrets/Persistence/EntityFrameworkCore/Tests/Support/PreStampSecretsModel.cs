using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// The Secrets model as builds from before the schema-version stamp had it: <see cref="PreReuseSecretsModel"/>, because
/// those builds also predate the tombstone, without the stamp column either. A context built with it writes a row the way
/// those builds did, with <see cref="WriteAsync"/>, into a database still at a migration before the stamp. It only writes:
/// migrating with it would report the stamp as a pending model change.
/// </summary>
internal sealed class PreStampSecretsModel(ModelCustomizerDependencies dependencies) : PreReuseSecretsModel(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<SecretRecord>().Ignore(record => record.SchemaVersion);
    }

    /// <summary>Makes <paramref name="options"/> build this model instead of the current one.</summary>
    public static DbContextOptionsBuilder<TContext> Use<TContext>(DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext =>
        options.ReplaceService<IModelCustomizer, PreStampSecretsModel>();

    /// <summary>
    /// Writes <paramref name="secret"/> through a context built with this model. The current repository cannot: it
    /// addresses rows by the tombstone this model does not have.
    /// </summary>
    public static async Task WriteAsync(DbContext context, Secret secret)
    {
        context.Add(SecretDocument.FromSecret(secret).ToRecord());
        await context.SaveChangesAsync();
    }
}
