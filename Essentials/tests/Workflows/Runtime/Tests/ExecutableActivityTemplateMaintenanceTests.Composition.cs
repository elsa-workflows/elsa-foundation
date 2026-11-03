using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class ExecutableActivityTemplateMaintenanceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_default_DI_shares_actual_atomic_material_and_source_state_across_scopes(bool repeatRegistration)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)); services.AddWorkflowRuntime();
        if (repeatRegistration) services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var templates = first.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>();
        var artifacts = first.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(); var references = first.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        Assert.True(templates.CanMaintainReferences(artifacts, references));
        var template = Template("bound"); var reference = Reference(template);
        Assert.Same(reference, await templates.SaveWithReferenceAsync(template, reference));
        Assert.Same(templates, second.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>());
        Assert.Same(artifacts, second.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.Same(references, second.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        Assert.Same(templates, second.ServiceProvider.GetRequiredService<IExecutableActivityTemplateReader>());
        Assert.Same(references, second.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceReader>());
        Assert.Same(template, await second.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(template.TemplateId));
        Assert.Same(reference, await second.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(reference.SourceReferenceId));
        Assert.False(await templates.TryDeleteUnreferencedAsync(template));
    }

    [Fact]
    public async Task Native_default_DI_keeps_separate_root_providers_storage_independent()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        await using var first = services.BuildServiceProvider(); await using var second = services.BuildServiceProvider();
        var template = Template("bound");
        await first.GetRequiredService<IExecutableActivityTemplateStore>().SaveWithReferenceAsync(template, Reference(template));
        Assert.Null(await second.GetRequiredService<IExecutableActivityTemplateStore>().FindAsync(template.TemplateId));
        Assert.Null(await second.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(Reference(template).SourceReferenceId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_default_DI_atomic_admission_uses_the_selected_clock(bool expired)
    {
        var clock = new FakeTimeProvider(Now); var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock); services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(); var templates = provider.GetRequiredService<IExecutableActivityTemplateStore>();
        var template = Template("bound"); var reference = Reference(template) with { ExpiresAt = Now.AddMinutes(expired ? 1 : 3) }; clock.Advance(TimeSpan.FromMinutes(2));
        if (expired) await Assert.ThrowsAsync<InvalidOperationException>(() => templates.SaveWithReferenceAsync(template, reference).AsTask());
        else Assert.Same(reference, await templates.SaveWithReferenceAsync(template, reference));
        Assert.Equal(expired, await templates.FindAsync(template.TemplateId) is null);
        Assert.Equal(expired, await provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(reference.SourceReferenceId) is null);
    }

    [Fact]
    public async Task Native_default_DI_collection_observes_the_selected_workflow_store()
    {
        var services = new ServiceCollection().AddWorkflowRuntime(); await using var provider = services.BuildServiceProvider();
        var templates = provider.GetRequiredService<IExecutableActivityTemplateStore>(); var artifacts = provider.GetRequiredService<IWorkflowExecutableStore>();
        var template = Template("bound"); await templates.SaveAsync(template);
        var workflow = new WorkflowExecutable(new("workflow", "definition", "version", "1", "workflow-hash"),
            Node("workflow", new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash }),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        await artifacts.SaveAsync(workflow);
        Assert.False(await templates.TryDeleteUnreferencedAsync((await templates.FindAsync(template.TemplateId))!));
        Assert.True(await artifacts.DeleteAsync(workflow.Identity.ArtifactId));
        Assert.True(await templates.TryDeleteUnreferencedAsync((await templates.FindAsync(template.TemplateId))!));
    }

    [Fact]
    public async Task Native_default_DI_source_maintenance_uses_the_selected_artifact_guard()
    {
        var services = new ServiceCollection().AddWorkflowRuntime(); await using var provider = services.BuildServiceProvider();
        var artifacts = provider.GetRequiredService<IWorkflowExecutableStore>(); var references = provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var reference = Reference(Template("artifact")) with { DeletedAt = Now, DeletedReason = "retired" };
        await references.SaveAsync(reference);
        await artifacts.SaveAsync(new(new(reference.ArtifactId, "definition", "version", "1", "workflow-hash"), Node("workflow"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        var guard = await artifacts.TryBeginDeletionAsync(reference.ArtifactId, "cleanup", Now.AddMinutes(1), Now);
        Assert.NotNull(guard); Assert.True(await references.TryDeleteDoomedAsync(reference, Now, guard!));
        Assert.Null(await references.FindAsync(reference.SourceReferenceId));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Native_default_DI_refuses_selected_scoped_replacements_without_capturing_them(bool references, bool registerFirst)
    {
        var services = new ServiceCollection();
        if (registerFirst) Replace();
        services.AddWorkflowRuntime();
        if (!registerFirst) Replace();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var templates = provider.GetRequiredService<IExecutableActivityTemplateStore>();
        using var scope = provider.CreateScope(); using var other = provider.CreateScope();
        var artifacts = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(); var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        Assert.False(templates.CanMaintainReferences(artifacts, sources));
        if (references) Assert.NotSame(sources, other.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        else Assert.NotSame(artifacts, other.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
        void Replace()
        {
            if (references) services.AddScoped<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
            else services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        }
    }

    [Fact]
    public void Native_default_DI_backend_owns_and_removes_the_group_and_aliases_without_touching_other_services()
    {
        var services = new ServiceCollection().AddWorkflowRuntime(); var unrelated = new object(); services.AddSingleton(unrelated);
        var backend = RuntimeArtifactStoreBackend.Find(services)!; Assert.Equal(RuntimeArtifactStoreBackend.InMemory, backend.Name);
        var group = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(InMemoryRuntimeArtifactStores));
        Assert.True(backend.Owns(group)); backend.EnsureOwnsRegisteredContracts(services); backend.RemoveOwnedArtifacts(services);
        Assert.DoesNotContain(group, services); Assert.Null(RuntimeArtifactStoreBackend.Find(services));
        using var provider = services.BuildServiceProvider(); Assert.Same(unrelated, provider.GetRequiredService<object>());
        Assert.Null(provider.GetService<IWorkflowExecutableStore>()); Assert.Null(provider.GetService<IExecutableActivityTemplateStore>());
        Assert.Null(provider.GetService<IWorkflowExecutableSourceReferenceStore>());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_default_DI_cohort_collector_refuses_mixed_template_operations_without_deleting_hidden_material(bool replaceReferences)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)); services.AddWorkflowRuntime();
        if (replaceReferences) services.AddScoped<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        else services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>();
        var artifacts = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(); var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var template = Template("mixed"); var reference = Reference(template).Retire(Now, "retired");
        await templates.SaveAsync(template); await sources.SaveAsync(reference);
        Assert.False(templates.CanMaintainReferences(artifacts, sources));
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts, sources, templates, new InMemoryWorkflowExecutionStateStore(), null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }), new FakeTimeProvider(Now),
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, scope.ServiceProvider.GetRequiredService<IStimulusDeliveryStore>());
        var result = await collector.SweepAsync();
        Assert.Equal(0, result.DeletedReferenceCount); Assert.Equal(0, result.DeletedActivityTemplateCount);
        Assert.Same(template, await templates.FindAsync(template.TemplateId)); Assert.NotNull(await sources.FindAsync(reference.SourceReferenceId));
    }

}
