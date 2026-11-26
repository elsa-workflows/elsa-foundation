using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.History;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryShellFeatureTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Public_shell_enrollment_qualifies_the_actual_admission_pair_in_either_registration_order(bool first, bool enabled)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "shell-history-controls-owned-signing-key-at-least-32-bytes");
        var feature = new WorkflowsRuntimeHistoryFeature { Enabled = enabled };
        if (first) feature.ConfigureServices(services);
        services.AddWorkflowRuntime();
        if (!first) feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(validateScopes: true); using var scope = provider.CreateScope();
        var selected = scope.ServiceProvider;
        var dispatcher = Assert.IsType<WorkflowStartDispatcher>(selected.GetRequiredService<IWorkflowStartDispatcher>());
        var admission = dispatcher.HistoryAdmission!.Invoke("shell-admission");
        Assert.Equal(enabled, admission.Enabled);
        if (enabled)
        {
            var policy = Assert.IsType<RuntimeHistoryCapturePolicy>(admission.Policy);
            Assert.True(policy.Required); Assert.Contains("BookmarkConsumed", policy.SupportedKinds);
            Assert.True(selected.GetRequiredService<IRuntimeCheckpointCommitStore>().CanCommitHistory(selected.GetRequiredService<IRuntimeHistoryStore>()));
        }
        else Assert.Null(admission.Policy);
        Assert.Single(selected.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<RuntimeHistoryCapture>());
    }

    [Fact]
    public void Disabled_shell_capture_preserves_an_explicit_reader_without_activating_it_for_new_admission()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddWorkflowRuntime();
        new WorkflowsRuntimeHistoryFeature { Enabled = false }.ConfigureServices(services);
        var reads = 0;
        services.AddScoped<IRuntimeHistoryStore>(_ => { reads++; throw new IOException("retained reader unavailable"); });
        using var provider = services.BuildServiceProvider(validateScopes: true); using var scope = provider.CreateScope();
        var dispatcher = Assert.IsType<WorkflowStartDispatcher>(scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>());
        var admission = dispatcher.HistoryAdmission!.Invoke("disabled-shell-admission");
        Assert.False(admission.Enabled); Assert.Null(admission.Policy); Assert.Equal(0, reads);
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("shell-history")); }
}
