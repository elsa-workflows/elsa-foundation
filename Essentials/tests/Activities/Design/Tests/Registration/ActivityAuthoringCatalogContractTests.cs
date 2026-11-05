using System.Security.Claims;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Models;
using Elsa.Tasks.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Registration;

/// <summary>The actor-gated Core catalog contract (spec 1193, FR-003) as the Activities Design API implements it.</summary>
public sealed class ActivityAuthoringCatalogContractTests
{
    [Fact]
    public async Task An_actor_with_design_read_gets_every_entry_with_the_designers_availability_versions_and_inputs()
    {
        var reader = new FixtureReader();
        using var provider = Build(reader);
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));
        var catalog = scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>();

        var result = await catalog.ListAsync();

        Assert.True(result.Allowed);
        // The designer's own read with every entry, addable or not; nothing is narrowed or reordered.
        Assert.Equal(ActivityCatalogAvailability.All, Assert.Single(reader.Requests).Availability);
        Assert.Equal(["version-available", "version-excluded"], result.Entries.Select(entry => entry.ActivityVersionId));
        var excluded = result.Entries[1];
        Assert.False(excluded.Available);
        Assert.Equal("Excluded by the effective activity availability policy.", excluded.AvailabilityReason);
        Assert.Equal("2.1.0", excluded.Version);
        Assert.Equal("Acme.Excluded", excluded.ActivityTypeKey);
        var credential = Assert.Single(result.Entries[0].Inputs, input => input.Name == "ApiKey");
        Assert.True(credential.IsCredential);
        Assert.True(credential.IsSensitive);
        var text = Assert.Single(result.Entries[0].Inputs, input => input.Name == "Text");
        Assert.False(text.IsCredential);
        Assert.False(text.IsMasked);
        Assert.True(credential.IsMasked);
        // A password-hinted input is masked by the designer although it is neither sensitive nor a credential.
        var password = Assert.Single(result.Entries[0].Inputs, input => input.Name == "Password");
        Assert.Equal(("password", false, true), (password.UiHint, password.IsSensitive, password.IsMasked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_actor_without_design_read_or_without_a_normalized_request_is_denied_without_a_catalog_read(bool request)
    {
        var reader = new FixtureReader();
        using var provider = Build(reader);
        using var scope = request ? Scope(provider, Principal("tenant-a", ActivityDesignPermissions.AuthorProvider)) : provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>();

        var result = await catalog.ListAsync();

        Assert.False(result.Allowed);
        Assert.Empty(result.Entries);
        Assert.False(await catalog.CanReadAsync());
        Assert.Empty(reader.Requests);
    }

    [Fact]
    public async Task The_read_gate_reads_no_entries()
    {
        var reader = new FixtureReader();
        using var provider = Build(reader);
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));

        Assert.True(await scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>().CanReadAsync());
        Assert.Empty(reader.Requests);
    }

    [Fact]
    public async Task A_failing_owner_read_is_unavailable_with_a_fixed_message_and_the_cause_kept_inside()
    {
        var cause = new InvalidOperationException("store offline: secret-connection-detail");
        using var provider = Build(new FixtureReader { Failure = cause });
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));

        var error = await Assert.ThrowsAsync<ActivityAuthoringCatalogUnavailableException>(() =>
            scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>().ListAsync().AsTask());

        Assert.Same(cause, error.InnerException);
        Assert.Equal(ActivityAuthoringCatalogUnavailableException.FixedMessage, error.Message);
        Assert.DoesNotContain("secret-connection-detail", error.Message);
    }

    [Fact]
    public async Task The_callers_cancellation_propagates_instead_of_becoming_unavailable()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new FixtureReader { Before = cancellation.Cancel };
        using var provider = Build(reader);
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>().ListAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task An_owner_cancellation_the_caller_did_not_ask_for_is_unavailable()
    {
        var cause = new OperationCanceledException("owner store timed out");
        using var provider = Build(new FixtureReader { Failure = cause });
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));

        var error = await Assert.ThrowsAsync<ActivityAuthoringCatalogUnavailableException>(() =>
            scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>().ListAsync().AsTask());
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public async Task The_gate_asks_for_the_principals_tenant_with_the_request_as_its_resource_like_the_designer_route()
    {
        var handler = new RecordingResourceHandler();
        using var provider = Build(new FixtureReader(), handler);
        using var scope = Scope(provider, Principal("tenant-a", ActivityDesignPermissions.Read));
        var catalog = scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>();

        Assert.True(await catalog.CanReadAsync());
        var asked = Assert.Single(handler.Contexts);
        Assert.Equal("tenant-a", asked.TenantId);
        Assert.Same(provider.GetRequiredService<IHttpContextAccessor>().HttpContext, asked.Resource);

        // A host resource handler that refuses the request refuses this read exactly as it refuses the route.
        handler.Deny = true;
        Assert.False(await catalog.CanReadAsync());
        Assert.False((await catalog.ListAsync()).Allowed);
    }

    [Fact]
    public async Task One_catalog_is_registered_and_repeated_configuration_keeps_one_validator()
    {
        var services = Services(new FixtureReader());
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Equal("HttpActivityAuthoringCatalog", Assert.Single(services, item => item.ServiceType == typeof(IActivityAuthoringCatalog)).ImplementationType?.Name);
        var validator = Assert.Single(provider.GetServices<IStartupTask>(), IsValidator);
        await validator.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_second_catalog_registration_fails_startup_with_a_clear_diagnostic()
    {
        var services = Services(new FixtureReader());
        services.AddScoped<IActivityAuthoringCatalog, ReplacementCatalog>();
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Assert.Single(provider.GetServices<IStartupTask>(), IsValidator).ExecuteAsync(CancellationToken.None));

        Assert.Contains(nameof(IActivityAuthoringCatalog), error.Message);
        Assert.Contains("multiple registrations", error.Message);
    }

    [Fact]
    public void A_second_registration_made_before_the_feature_fails_its_configuration_without_the_tasks_feature()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityAuthoringCatalog, ReplacementCatalog>();
        services.AddScoped<IActivityAuthoringCatalog, ReplacementCatalog>();

        var error = Assert.Throws<InvalidOperationException>(() => new ActivitiesDesignApiFeature().ConfigureServices(services));

        Assert.Contains(nameof(IActivityAuthoringCatalog), error.Message);
    }

    [Fact]
    public async Task A_copied_child_shell_rebinds_the_validator_to_its_own_registrations()
    {
        var parent = Services(new FixtureReader());
        IServiceCollection child = new ServiceCollection();
        foreach (var item in parent) child.Add(item);
        new ActivitiesDesignApiFeature().ConfigureServices(child);
        child.AddScoped<IActivityAuthoringCatalog, ReplacementCatalog>();
        using var parentProvider = parent.BuildServiceProvider();
        using var childProvider = child.BuildServiceProvider();

        await Assert.Single(parentProvider.GetServices<IStartupTask>(), IsValidator).ExecuteAsync(CancellationToken.None);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Assert.Single(childProvider.GetServices<IStartupTask>(), IsValidator).ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(IActivityAuthoringCatalog), error.Message);
        Assert.Single(parent, item => item.ServiceType == typeof(IActivityAuthoringCatalog));
    }

    [Fact]
    public async Task A_host_replacement_registered_first_is_the_one_catalog()
    {
        var services = Services(new FixtureReader(), host => host.AddScoped<IActivityAuthoringCatalog, ReplacementCatalog>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ReplacementCatalog>(scope.ServiceProvider.GetRequiredService<IActivityAuthoringCatalog>());
        await Assert.Single(provider.GetServices<IStartupTask>(), IsValidator).ExecuteAsync(CancellationToken.None);
    }

    private static bool IsValidator(IStartupTask task) => task.GetType().Name == "ActivityAuthoringCatalogStartupTask";

    private static ServiceProvider Build(FixtureReader reader, IPermissionResourceHandler? handler = null)
        => Services(reader, handler is null ? null : host => host.AddSingleton(handler)).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static ServiceCollection Services(FixtureReader reader, Action<IServiceCollection>? host = null)
    {
        var services = new ServiceCollection();
        host?.Invoke(services);
        new PrimitivesFeature().ConfigureServices(services);
        services.AddSingleton<IInlineEventPublisher, StubEventPublisher>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        // The catalog projection itself has its own reader tests; this pins the gate and the Core mapping.
        services.AddScoped<IActivityAuthoringCatalogReader>(_ => reader);
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        return services;
    }

    private static IServiceScope Scope(ServiceProvider provider, ClaimsPrincipal principal)
    {
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = principal };
        return provider.CreateScope();
    }

    private static ClaimsPrincipal Principal(string tenantId, params string[] permissions) => new(new ClaimsIdentity(
    [
        new Claim(IdentityClaimTypes.Normalized, "v1"),
        new Claim(IdentityClaimTypes.TenantId, tenantId),
        new Claim(ClaimTypes.NameIdentifier, "actor-a"),
        ..permissions.Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
    ], "test"));

    private sealed class FixtureReader : IActivityAuthoringCatalogReader
    {
        public List<ListActivityAuthoringCatalog> Requests { get; } = [];
        public Exception? Failure { get; init; }
        public Action? Before { get; init; }

        public Task<ActivityAuthoringCatalogView> ListAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Before?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ActivityAuthoringCatalogView([
                Entry("version-available", "Acme.Available", "1.0.0", true, null,
                    [Input("Text", isCredential: false), Input("ApiKey", isCredential: true), Input("Password", isCredential: false, uiHint: "password")]),
                Entry("version-excluded", "Acme.Excluded", "2.1.0", false, "Excluded by the effective activity availability policy.", [])
            ]));
        }

        private static ActivityAuthoringDescriptorView Entry(string versionId, string typeKey, string version, bool available, string? reason,
            IReadOnlyCollection<ActivityInputDescriptorView> inputs) => new(versionId, typeKey, version, typeKey, "Acme", null, "Activity",
            available, reason, inputs, [], [], null, new("activity", versionId, new Dictionary<string, ActivityArgumentValue>(),
                new Dictionary<string, ActivityArgumentValue>(), null));

        private static ActivityInputDescriptorView Input(string name, bool isCredential, string? uiHint = null) => new(name, name, "String", CollectionKind.Single,
            name, null, 0, null, true, false, true, uiHint, null, null, null, IsSensitive: isCredential, IsCredential: isCredential);
    }

    private sealed class ReplacementCatalog : IActivityAuthoringCatalog
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ActivityAuthoringCatalogResult.Denied);
    }

    private sealed class RecordingResourceHandler : IPermissionResourceHandler
    {
        public List<PermissionEvaluationContext> Contexts { get; } = [];
        public bool Deny { get; set; }

        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (context.Permission != ActivityDesignPermissions.Read) return ValueTask.FromResult<PermissionEvaluationResult?>(null);
            Contexts.Add(context);
            return ValueTask.FromResult<PermissionEvaluationResult?>(Deny ? PermissionEvaluationResult.Denied() : null);
        }
    }

    private sealed class StubEventPublisher : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
