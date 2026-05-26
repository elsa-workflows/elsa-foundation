using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Extensions;
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

            PreventImmutableChanges(entries);
            ApplyTimestamps(entries);

            using var scope = ServiceProvider.CreateScope();
            await ApplyGlobalSavingHandlers(entries, scope, cancellationToken);
            await ApplyEntitySavingHandlers(entries, scope, cancellationToken);
        }


        /// <summary>
        /// Stamps <see cref="Entity.CreatedAt"/> and <see cref="Entity.LastModifiedAt"/> on tracked entities.
        /// Runs after <see cref="PreventImmutableChanges"/> so that if anything ever bypasses the
        /// immutability guard, a fresh <see cref="Entity.LastModifiedAt"/> on a row whose
        /// <see cref="Entity.CreatedAt"/> is earlier becomes the forensic signal.
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

        private static void PreventImmutableChanges(IEnumerable<EntityEntry<Entity>> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.State != EntityState.Modified)
                    continue;

                var modifiedProperties = entry.Properties
                    .Where(x => x.IsModified)
                    .Select(x => x.Metadata.Name);

                var immutableProperties = entry.Entity
                    .GetType()
                    .GetImmutableProperties();

                var prohibitedImmutableProperties = modifiedProperties.Where(immutableProperties.Contains);
                if (prohibitedImmutableProperties.Any())
                {
                    throw new InvalidOperationException(
                        $"Properties '{string.Join(", ", prohibitedImmutableProperties)}' are immutable"
                    );
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

        private async Task ApplyEntitySavingHandlers(IEnumerable<EntityEntry<Entity>> entries, IServiceScope scope, CancellationToken cancellationToken)
        {
            foreach (var entry in entries.Where(IsModifiedEntity))
            {
                var handlerType = typeof(IEntitySavingHandler<,>).MakeGenericType(
                    GetType(),
                    entry.Entity.GetType()
                );
                var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();
                foreach (var handler in handlers)
                {
                    var method = handlerType.GetMethod(nameof(IEntitySavingHandler<,>.Handle));
                    if (method is not null)
                    {
                        var task = (ValueTask)method.Invoke(handler, [this, entry.Entity, cancellationToken])!;
                        await task;
                    }
                }
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
            ApplyEntityModelCreatingHandlers(this, builder);
            ApplyImmutability(builder);
        }

        private static void ApplyImmutability(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var entityType = entity.ClrType;

                if (entityType is null)
                    continue;

                var immutableProperties = entityType.GetImmutableProperties();
                foreach (var propertyName in immutableProperties)
                {
                    ApplyPropertyImmutability(entity, propertyName);
                }
            }
        }

        private static void ApplyPropertyImmutability(IMutableEntityType entity, string propertyName)
        {
            var property = entity.FindProperty(propertyName);
            property?.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

            var navigation = entity.FindNavigation(propertyName);
            if (navigation?.TargetEntityType.IsOwned() != true)
                return;

            foreach (var ownedProperty in navigation.TargetEntityType.GetProperties())
            {
                ownedProperty.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
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
        #endregion
    }
}
