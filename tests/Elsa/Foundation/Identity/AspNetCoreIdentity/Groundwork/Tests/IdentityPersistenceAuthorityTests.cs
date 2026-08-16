using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Composition;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests
{
    public sealed class IdentityPersistenceAuthorityTests
    {
        private const string GroundworkAuthority = "FoundationIdentityAspNetCoreIdentityGroundwork";
        private const string EntityFrameworkAuthority = "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore";
        private const string UnmarkedStoreAuthority = "UnmarkedAspNetCoreIdentityUserOrRoleStore";

        [Fact]
        public void Groundwork_is_the_single_selected_authority()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddFoundationAspNetCoreIdentityGroundwork();

            var selection = Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityPersistenceAuthority))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<IdentityPersistenceAuthority>());
            Assert.Equal(GroundworkAuthority, selection.FeatureName);
        }

        [Fact]
        public void Selecting_groundwork_twice_is_idempotent()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.AddFoundationAspNetCoreIdentityGroundwork();

            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(IdentityPersistenceAuthority));
            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(IConfigureOptions<IdentityOptions>) &&
                descriptor.ImplementationFactory is not null);
        }

        [Fact]
        public void Provider_neutral_identity_services_do_not_claim_a_persistence_authority()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentity();

            services.AddFoundationAspNetCoreIdentityGroundwork();

            var selection = Assert.Single(services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityPersistenceAuthority))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<IdentityPersistenceAuthority>());
            Assert.Equal(GroundworkAuthority, selection.FeatureName);
        }

        [Fact]
        public void Frozen_ef_selected_before_groundwork_is_rejected_immediately()
        {
            var services = new ServiceCollection();
            services.AddSingleton<EntityFrameworkCore.CompatibilityProbe.FrozenEfIdentityStore>();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                services.AddFoundationAspNetCoreIdentityGroundwork());

            AssertAuthorityConflict(exception, EntityFrameworkAuthority, GroundworkAuthority);
        }

        [Fact]
        public void An_unmarked_user_store_selected_before_groundwork_is_rejected_before_replacement()
        {
            IServiceCollection services = new ServiceCollection();
            var customStore = ServiceDescriptor.Scoped<IUserStore<AspNetCoreIdentityUser>>(_ => null!);
            services.Add(customStore);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                services.AddFoundationAspNetCoreIdentityGroundwork());

            AssertAuthorityConflict(exception, GroundworkAuthority, UnmarkedStoreAuthority);
            Assert.Contains(customStore, services);
        }

        [Fact]
        public void An_unmarked_role_store_selected_before_groundwork_is_rejected_before_replacement()
        {
            IServiceCollection services = new ServiceCollection();
            var customStore = ServiceDescriptor.Scoped<IRoleStore<IdentityRole>>(_ => null!);
            services.Add(customStore);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                services.AddFoundationAspNetCoreIdentityGroundwork());

            AssertAuthorityConflict(exception, GroundworkAuthority, UnmarkedStoreAuthority);
            Assert.Contains(customStore, services);
        }

        [Fact]
        public void Frozen_ef_selected_after_groundwork_is_rejected_when_identity_options_are_resolved()
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.AddSingleton<EntityFrameworkCore.CompatibilityProbe.FrozenEfIdentityStore>();
            using var provider = services.BuildServiceProvider();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IOptions<IdentityOptions>>().Value);

            AssertAuthorityConflict(exception, EntityFrameworkAuthority, GroundworkAuthority);
        }

        [Fact]
        public void An_unmarked_store_added_after_groundwork_is_rejected_by_final_composition_validation()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.Add(ServiceDescriptor.Scoped<IUserStore<AspNetCoreIdentityUser>>(_ => null!));
            using var provider = services.BuildServiceProvider();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                provider.GetRequiredService<IOptions<IdentityOptions>>().Value);

            AssertAuthorityConflict(exception, GroundworkAuthority, UnmarkedStoreAuthority);
        }

        [Fact]
        public async Task Frozen_ef_selected_after_groundwork_is_rejected_by_plain_host_startup()
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.AddSingleton<EntityFrameworkCore.CompatibilityProbe.FrozenEfIdentityStore>();
            using var provider = services.BuildServiceProvider();
            var hostedGuard = Assert.Single(provider.GetServices<IHostedService>(), service =>
                service.GetType().Name.Contains("PersistenceAuthorityGuard", StringComparison.Ordinal));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                hostedGuard.StartAsync(CancellationToken.None));

            AssertAuthorityConflict(exception, EntityFrameworkAuthority, GroundworkAuthority);
        }

        [Fact]
        public async Task Frozen_ef_selected_after_groundwork_is_rejected_by_shell_initialization()
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityGroundwork();
            services.AddSingleton<EntityFrameworkCore.CompatibilityProbe.FrozenEfIdentityStore>();
            using var provider = services.BuildServiceProvider();
            var shellGuard = Assert.Single(provider.GetServices<IShellInitializer>(), initializer =>
                initializer.GetType().Name.Contains("PersistenceAuthorityGuard", StringComparison.Ordinal));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                shellGuard.InitializeAsync(CancellationToken.None));

            AssertAuthorityConflict(exception, EntityFrameworkAuthority, GroundworkAuthority);
        }

        [Fact]
        public void A_second_marker_authority_is_rejected_without_provider_dependencies()
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityGroundwork();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                services.SelectIdentityPersistenceAuthority("SyntheticIdentityProvider"));

            AssertAuthorityConflict(exception, GroundworkAuthority, "SyntheticIdentityProvider");
        }

        private static void AssertAuthorityConflict(
            InvalidOperationException exception,
            string firstAuthority,
            string secondAuthority)
        {
            Assert.Contains("persistence authority conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(firstAuthority, exception.Message, StringComparison.Ordinal);
            Assert.Contains(secondAuthority, exception.Message, StringComparison.Ordinal);
        }
    }
}

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.CompatibilityProbe
{
    public sealed class FrozenEfIdentityStore;
}
