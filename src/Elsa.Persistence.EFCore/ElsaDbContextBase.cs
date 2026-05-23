using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore
{
    /// <summary>
    /// An optional base class to implement with some opinions on certain converters to install for certain DB providers.
    /// </summary>
    public abstract class ElsaDbContextBase : DbContext, IElsaDbContextSchema
    {
        private static readonly HashSet<EntityState> ModifiedEntityStates =
        [
            EntityState.Added,
            EntityState.Modified,
        ];

        protected IServiceProvider ServiceProvider { get; }
        private readonly ElsaDbContextOptions? _elsaDbContextOptions;

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
            await OnBeforeSavingAsync(cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (!string.IsNullOrWhiteSpace(Schema))
                modelBuilder.HasDefaultSchema(Schema);

            var additionalConfigurations = _elsaDbContextOptions?.GetModelConfigurations(this);
            additionalConfigurations?.Invoke(modelBuilder);

            using var scope = ServiceProvider.CreateScope();

            ApplyEntityModelCreatingHandlers(scope, modelBuilder);
            ApplyImmutability(modelBuilder);
        }

        protected virtual void ApplyImmutability(ModelBuilder modelBuilder)
        {
            foreach(var entity in modelBuilder.Model.GetEntityTypes())
            {
                var entityType = entity.ClrType;

                if (entityType is null)
                    continue;

                var immutableProperties = entityType.GetImmutableProperties();
                foreach(var propertyName in immutableProperties)
                {
                    var property = entity.GetProperty(propertyName);
                    property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
                }
            }
        }

        private void ApplyEntityModelCreatingHandlers(IServiceScope scope, ModelBuilder modelBuilder)
        {
            var entityTypeHandlers = scope.ServiceProvider.GetServices<IEntityModelCreatingHandler>().ToList();

            foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
            {
                foreach (var handler in entityTypeHandlers)
                    handler.Handle(this, modelBuilder, entityType);
            }
        }

        private async Task OnBeforeSavingAsync(CancellationToken cancellationToken)
        {
            PreventImmutableChanges();

            using var scope = ServiceProvider.CreateScope();
            await ApplyGlobalSavingHandlers(scope, cancellationToken);
            await ApplyEntitySavingHandlers(scope, cancellationToken);
        }

        void PreventImmutableChanges()
        {
            foreach(var entry in ChangeTracker.Entries())
            {
                if(entry.State != EntityState.Modified)
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


        private async Task ApplyGlobalSavingHandlers(IServiceScope scope, CancellationToken cancellationToken)
        {
            var handlers = scope.ServiceProvider.GetServices<IGlobalEntitySavingHandler>().ToList();
            foreach (var entry in ChangeTracker.Entries().Where(IsModifiedEntity))
            {
                foreach (var handler in handlers)
                    await handler.HandleAsync(this, entry, cancellationToken);
            }
        }

        private async Task ApplyEntitySavingHandlers(IServiceScope scope, CancellationToken cancellationToken)
        {
            foreach (var entry in ChangeTracker.Entries().Where(IsModifiedEntity))
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
    }
}
