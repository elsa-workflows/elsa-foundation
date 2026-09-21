using System.Reflection;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Elsa.Workflows.Design.Reconciliation.Git.Startup;
using Elsa.Workflows.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Feature composition: the git feature registers the source + the base import lifecycle for both roles,
/// and the export reconciler + export task only for a Writer (spec 085 R2/R3/T026).
/// </summary>
public sealed class WorkflowsDesignGitReconciliationFeatureTests
{
    private static ServiceCollection Configure(GitReconciliationRole role)
    {
        var services = new ServiceCollection();
        new WorkflowsDesignGitReconciliationFeature { RemoteUrl = "git@example.com:acme/wf.git", Role = role }
            .ConfigureServices(services);
        return services;
    }

    private static bool Registers(IServiceCollection services, Type service, Type implementation) =>
        services.Any(d => d.ServiceType == service && d.ImplementationType == implementation);

    [Fact]
    public void Registers_git_source_and_base_import_startup_task_for_any_role()
    {
        var services = Configure(GitReconciliationRole.Consumer);

        Assert.True(Registers(services, typeof(IWorkflowReconciliationSource), typeof(GitWorkflowReconciliationSource)));
        Assert.True(Registers(services, typeof(IGitWorkspace), typeof(GitWorkspace)));
        Assert.True(Registers(services, typeof(IStartupTask), typeof(WorkflowsVersionReconcilerStartupTask)));
    }

    [Fact]
    public void Consumer_role_registers_no_exporter_or_export_task()
    {
        var services = Configure(GitReconciliationRole.Consumer);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IGitWorkflowExporter));
        Assert.False(Registers(services, typeof(IStartupTask), typeof(GitWorkflowExportStartupTask)));
    }

    [Fact]
    public void Writer_role_registers_exporter_and_export_task()
    {
        var services = Configure(GitReconciliationRole.Writer);

        Assert.True(Registers(services, typeof(IGitWorkflowExporter), typeof(GitWorkflowExporter)));
        Assert.True(Registers(services, typeof(IStartupTask), typeof(GitWorkflowExportStartupTask)));
    }

    [Fact]
    public void Token_setting_is_marked_secret()
    {
        // Asserted by attribute-name reflection so the test need not reference the manifest-generator package.
        var property = typeof(WorkflowsDesignGitReconciliationFeature).GetProperty(nameof(WorkflowsDesignGitReconciliationFeature.Token))!;
        var setting = property.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "ManifestSettingAttribute");

        Assert.NotNull(setting);
        var secret = setting!.GetType().GetProperty("Secret")?.GetValue(setting);
        Assert.Equal(true, secret);
    }
}
