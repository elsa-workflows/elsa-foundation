using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Persistence.EFCore.Options;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore
{
    /// <summary>
    /// An optional base class to implement with some opinions on certain converters to install for certain DB providers.
    /// </summary>
    public abstract class ElsaDbContextBase : DbContext, IElsaDbContextSchema
    {
        private readonly ElsaDbContextOptions? _elsaDbContextOptions;

        protected IServiceProvider ServiceProvider { get; }


        /// <summary>
        /// The default schema used by Elsa.
        /// </summary>
        public static string ElsaSchema { get; set; } = "Elsa";

        /// <inheritdoc/>
        public string Schema { get; }

        /// <summary>
        /// The table used to store the migrations history.
        /// </summary>
        public static string MigrationsHistoryTable { get; set; } = "__EFMigrationsHistory";

        /// <summary>
        /// Initializes a new instance of the <see cref="ElsaDbContextBase"/> class.
        /// </summary>
        protected ElsaDbContextBase(DbContextOptions options, IServiceProvider serviceProvider) : base(options)
        {
            ServiceProvider = serviceProvider;
            _elsaDbContextOptions = options.FindExtension<ElsaDbContextOptionsExtension>()?.Options;

            // ReSharper disable once VirtualMemberCallInConstructor
            Schema = !string.IsNullOrWhiteSpace(_elsaDbContextOptions?.SchemaName)
                ? _elsaDbContextOptions.SchemaName
                : ElsaSchema;
        }

        /// <inheritdoc/>
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await BeforeSavingChanges(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureEntityModel(modelBuilder);
        }        


        #region SAVE HANDLING
        private static readonly HashSet<EntityState> ModifiedEntityStates =
        [
            EntityState.Added,
            EntityState.Modified,
        ];

        private async Task BeforeSavingChanges(CancellationToken cancellationToken)
        {
            var entries = ChangeTracker.Entries<Entity>();

            ApplyTimestamps(entries);

            using var scope = ServiceProvider.CreateScope();
            await ApplyGlobalSavingHandlers(entries, scope, cancellationToken);
            await DispatchEntitySavingEvents(entries, scope, cancellationToken);
        }

        /// <summary>
        /// Publishes <see cref="OnEntitySaving"/> for each modified <see cref="Entity"/>. The single
        /// <c>ApplyEntitySavingHandlers</c> aggregator is the sole subscriber and dispatches every
        /// registered <see cref="IEntitySavingHandler{TDbContext,TEntity}"/> contributor. The
        /// unrelated <see cref="IGlobalEntitySavingHandler"/> (runs for every entity, no per-type
        /// fan-in) keeps its own dispatch path above.
        /// </summary>
        private async Task DispatchEntitySavingEvents(IEnumerable<EntityEntry<Entity>> entries, IServiceScope scope, CancellationToken cancellationToken)
        {
            var sender = scope.ServiceProvider.GetService<IEventPublisher>();
            if (sender is null)
                return;

            foreach (var entry in entries.Where(IsModifiedEntity))
                await sender.Publish(new OnEntitySaving(this, entry), cancellationToken: cancellationToken);
        }


        /// <summary>
        /// Stamps <see cref="Entity.CreatedAt"/> and <see cref="Entity.LastModifiedAt"/> on tracked entities.
        /// <see cref="Entity.LastModifiedAt"/> advancing past <see cref="Entity.CreatedAt"/> on a
        /// write-once entity is a forensic signal that something bypassed the EF Core
        /// <c>PropertySaveBehavior.Throw</c> guard.
        /// </summary>
        private void ApplyTimestamps(IEnumerable<EntityEntry<Entity>> entries)
        {
            using var scope = ServiceProvider.CreateScope();
            var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
            var now = clock.UtcNow;

            foreach (var entry in entries)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = now;
                        entry.Entity.LastModifiedAt = now;
                        break;

                    case EntityState.Modified:
                        entry.Entity.LastModifiedAt = now;
                        break;
                }
            }
        }

        private async Task ApplyGlobalSavingHandlers(IEnumerable<EntityEntry<Entity>> entries, IServiceScope scope, CancellationToken cancellationToken)
        {
            var handlers = scope.ServiceProvider.GetServices<IGlobalEntitySavingHandler>().ToList();
            foreach (var entry in entries.Where(IsModifiedEntity))
            {
                foreach (var handler in handlers)
                    await handler.Handle(this, entry, cancellationToken);
            }
        }

        /// <summary>
        /// Determine if an entity was modified.
        /// </summary>
        private bool IsModifiedEntity(EntityEntry entityEntry)
        {
            return ModifiedEntityStates.Contains(entityEntry.State) && entityEntry.Entity is Entity;
        }
        #endregion

        #region MODEL BUILDING
        private void ConfigureEntityModel(ModelBuilder builder)
        {
            if (!string.IsNullOrWhiteSpace(Schema))
                builder.HasDefaultSchema(Schema);

            var additionalConfigurations = _elsaDbContextOptions?.GetModelConfigurations(this);
            additionalConfigurations?.Invoke(builder);

            // Order is important. SQLite does not support RowNumber as non-ID column and it ignores the RowNumber property; hence this must be done before the ignore.
            ApplyRowNumberIndex(builder);
            ApplyTenantIdIndex(builder);
            ApplyEntityModelCreatingHandlers(this, builder);
            ApplyBaseEntityImmutability(builder);
        }

        /// <summary>
        /// Marks <see cref="Entity.RowNumber"/> and <see cref="Entity.CreatedAt"/> as
        /// <c>PropertySaveBehavior.Throw</c> on every entity type that derives from
        /// <see cref="Entity"/>. Domain-specific write-once properties are configured
        /// explicitly in their respective <c>IEntityTypeConfiguration&lt;T&gt;</c>.
        /// </summary>
        private static void ApplyBaseEntityImmutability(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(Entity).IsAssignableFrom(entity.ClrType))
                    continue;

                entity.FindProperty(nameof(Entity.RowNumber))
                      ?.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
                entity.FindProperty(nameof(Entity.CreatedAt))
                      ?.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            }
        }

        private void ApplyEntityModelCreatingHandlers(ElsaDbContextBase dbContext, ModelBuilder modelBuilder)
        {
            using var scope = ServiceProvider.CreateScope();
            var entityTypeHandlers = scope.ServiceProvider.GetServices<IEntityModelCreatingHandler>().ToList();

            foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
            {
                foreach (var handler in entityTypeHandlers)
                    handler.Handle(dbContext, modelBuilder, entityType);
            }
        }

        private static void ApplyRowNumberIndex(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(Entity).IsAssignableFrom(entity.ClrType))
                {
                    modelBuilder.Entity(entity.ClrType)
                        .HasIndex(nameof(Entity.RowNumber))
                        .IsUnique();
                }
            }
        }

        private static void ApplyTenantIdIndex(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(TenantEntity).IsAssignableFrom(entity.ClrType))
                {
                    modelBuilder.Entity(entity.ClrType)
                        .HasIndex(nameof(TenantEntity.TenantId));
                }
            }
        }
        #endregion
    }
}
