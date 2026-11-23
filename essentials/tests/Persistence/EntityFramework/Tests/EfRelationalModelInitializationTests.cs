using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfRelationalModelInitializationTests
{
    [Fact]
    public void Another_first_reader_cannot_remove_the_factory_between_the_migration_readers_lookups()
    {
        var options = Options().EnableServiceProviderCaching(false).Options;
        using var context = new ColdContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var reader = DispatchProxy.Create<IModel, ConcurrentReader>();
        ((ConcurrentReader)(object)reader).Model = model;

        Assert.Single(reader.GetRelationalModel().Tables);
    }

    [Fact]
    public async Task Independent_contexts_can_read_a_cold_shared_design_model_concurrently()
    {
        var options = Options().Options;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 24).Select(async _ =>
        {
            await start.Task;
            using var context = new ColdContext(options);
            var model = context.GetService<IDesignTimeModel>().Model;
            return model.GetRelationalModel();
        }).ToArray();
        start.SetResult();
        var models = await Task.WhenAll(tasks);

        Assert.All(models, model => Assert.Same(models[0], model));
        Assert.Single(models[0].Tables);
    }

    private static DbContextOptionsBuilder<ColdContext> Options()
    {
        var options = new DbContextOptionsBuilder<ColdContext>();
        EfRelationalProviderBinding.UseSqlite(options, "Data Source=:memory:", "__EFMigrationsHistory_ModelInitialization");
        return options;
    }

    private sealed class ColdContext(DbContextOptions<ColdContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Row>().HasKey(row => row.Id);
    }

    public class ConcurrentReader : DispatchProxy
    {
        public IModel Model { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var result = method!.Invoke(Model, arguments);
            if (method.Name == nameof(IModel.FindRuntimeAnnotationValue) &&
                arguments is ["Relational:RelationalModel"] && result is null)
            {
                // Deterministically interleave another context's first read after this reader observes
                // no relational model, but before EF asks for the factory that the other reader removes.
                _ = Model.GetRelationalModel();
            }
            return result;
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }
}
