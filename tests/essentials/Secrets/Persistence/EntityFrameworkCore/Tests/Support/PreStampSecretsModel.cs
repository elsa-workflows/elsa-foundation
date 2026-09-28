using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// The Secrets model as builds from before the schema-version stamp had it: the same, without the stamp column. A
/// context built with it writes a row the way those builds did, through the real repository, into a database still at
/// a migration before the stamp. It only writes: migrating with it would report the stamp as a pending model change.
/// </summary>
internal sealed class PreStampSecretsModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
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
}
