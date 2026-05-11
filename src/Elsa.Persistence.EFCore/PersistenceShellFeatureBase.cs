using CShells.Features;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Options;
using Elsa.Persistence.EFCore.Tasks;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Elsa.Persistence.EFCore
{
    public abstract class PersistenceShellFeatureBase<TDbContext> : IShellFeature
        where TDbContext : DbContext
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use context pooling.
        /// When not explicitly set, falls back to shared settings if available.
        /// </summary>
        public bool? UseContextPooling { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to run migrations.
        /// When not explicitly set, falls back to shared settings if available, defaulting to true.
        /// </summary>
        public bool? RunMigrations { get; set; }

        /// <summary>
        /// Gets or sets the lifetime of the <see cref="IDbContextFactory{TContext}"/>.
        /// When not explicitly set, falls back to shared settings if available, defaulting to <see cref="ServiceLifetime.Scoped"/>.
        /// </summary>
        public ServiceLifetime? DbContextFactoryLifetime { get; set; }

        /// <summary>
        /// Gets or sets the connection string to use for the database.
        /// When not explicitly set, falls back to shared settings if available.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool UseCommands { get; set; } = true;

        /// <summary>
        /// 
        /// </summary>
        public bool UseQueries { get; set; } = true;

        /// <summary>
        /// Gets or sets additional options to configure the database context.
        /// When not explicitly set, falls back to shared settings if available.
        /// </summary>
        public ElsaDbContextOptions? DbContextOptions { get; set; }

        /// <summary>
        /// Gets or sets the callback used to configure the <see cref="DbContextOptionsBuilder"/>.
        /// </summary>
        protected virtual Action<IServiceProvider, DbContextOptionsBuilder> DbContextOptionsBuilder { get; set; } = (_, _) => { };


        public void ConfigureServices(IServiceCollection services)
        {
            // Resolve pooling and lifetime settings with fallback
            // Note: These are resolved at configuration time, not runtime, but they'll use defaults if not set
            var useContextPooling = UseContextPooling ?? false;
            var dbContextFactoryLifetime = DbContextFactoryLifetime ?? ServiceLifetime.Scoped;
            var runMigrations = RunMigrations ?? true;

            if (useContextPooling)
                services.AddPooledDbContextFactory<TDbContext>(SetupDbContextOptionsBuilder);
            else
                services.AddDbContextFactory<TDbContext>(SetupDbContextOptionsBuilder, dbContextFactoryLifetime);

            services.Configure<MigrationOptions>(options =>
            {
                options.RunMigrations[$"{typeof(TDbContext).FullName}"] = runMigrations;
            });

            services.AddScoped<IStartupTask, RunMigrationsStartupTask<TDbContext>>();

            if (UseCommands)
            {
                services
                    .ConfigureCommands<TDbContext>();
            }
            if (UseQueries)
            {
                services.ConfigureQueries<TDbContext>();
            }

            OnConfiguring(services);
        }

        // Resolve effective settings at runtime, falling back to shared settings
        private void SetupDbContextOptionsBuilder(IServiceProvider serviceProvider, DbContextOptionsBuilder dbContextOptionsBuilder)
        {
            var sharedSettings = serviceProvider.GetService<IOptions<SharedPersistenceSettings>>()?.Value;

            var connectionString = ConnectionString
                ?? sharedSettings?.ConnectionString
                ?? throw new InvalidOperationException(
                    $"Connection string not configured for {GetType().Name}. " +
                    $"Either configure the feature directly or provide shared settings via the combined persistence feature.");

            var dbContextOptions = DbContextOptions ?? sharedSettings?.DbContextOptions;

            dbContextOptionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

            // Configure the database provider
            var migrationsAssembly = GetMigrationsAssembly();
            ConfigureProvider(dbContextOptionsBuilder, migrationsAssembly, connectionString, dbContextOptions);

            // Allow derived classes to further configure
            DbContextOptionsBuilder(serviceProvider, dbContextOptionsBuilder);
        }

        /// <summary>
        /// Gets the assembly containing migrations for this provider.
        /// By default, returns the assembly of the concrete feature type.
        /// </summary>
        protected virtual Assembly GetMigrationsAssembly() => GetType().Assembly;

        /// <summary>
        /// Configures the database provider for the specified <see cref="DbContextOptionsBuilder"/>.
        /// </summary>
        /// <param name="builder">The options builder to configure.</param>
        /// <param name="migrationsAssembly">The assembly containing migrations.</param>
        /// <param name="connectionString">The connection string to use.</param>
        /// <param name="options">Additional options to configure the database context.</param>
        protected abstract void ConfigureProvider(
            DbContextOptionsBuilder builder,
            Assembly migrationsAssembly,
            string connectionString,
            ElsaDbContextOptions? options
        );

        protected virtual void OnConfiguring(IServiceCollection services)
        {
        }
    }
}
