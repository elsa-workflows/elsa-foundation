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
using Microsoft.Extensions.Options;
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
    public void Registers_one_clone_slot_per_shell_that_the_shell_container_releases()
    {
        var registration = Assert.Single(Configure(GitReconciliationRole.Writer), d => d.ServiceType == typeof(GitCloneSlot));

        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        Assert.NotNull(registration.ImplementationFactory); // created, and so disposed, by the container
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

    private static IServiceCollection ConfigureToken(string token, string remoteUrl = "https://git.example.test/acme/wf.git")
    {
        var services = new ServiceCollection();
        new WorkflowsDesignGitReconciliationFeature { RemoteUrl = remoteUrl, CredentialsMode = GitCredentialsMode.Token, Token = token }
            .ConfigureServices(services);
        return services;
    }

    [Theory]
    [InlineData("s3cr3t\n", "s3cr3t")]
    [InlineData("s3cr3t\r\n", "s3cr3t")]
    [InlineData("s3cr3t", "s3cr3t")]
    public void A_trailing_line_break_is_no_part_of_the_token(string configured, string expected)
    {
        var options = (IOptions<GitReconciliationOptions>)ConfigureToken(configured)
            .Single(d => d.ServiceType == typeof(IOptions<GitReconciliationOptions>)).ImplementationInstance!;

        Assert.Equal(expected, options.Value.Token);
    }

    [Theory]
    [InlineData("ab\ncd")]
    [InlineData("ab\rcd")]
    [InlineData("ab\0cd")]
    [InlineData("ab\ncd\n")]
    public void A_token_holding_a_line_break_or_nul_fails_registration(string token)
    {
        Assert.Throws<InvalidOperationException>(() => ConfigureToken(token));
    }

    [Fact]
    public void A_line_break_in_the_token_is_not_checked_outside_token_mode()
    {
        var feature = new WorkflowsDesignGitReconciliationFeature { RemoteUrl = "git@example.com:acme/wf.git", Token = "ab\ncd" };

        Assert.Null(Record.Exception(() => feature.ConfigureServices(new ServiceCollection())));
    }

    [Theory]
    [InlineData("git@example.com:acme/wf.git")]
    [InlineData("ssh://git@example.com/acme/wf.git")]
    [InlineData("/srv/git/wf.git")]
    [InlineData("")]
    public void Token_mode_with_a_remote_that_is_not_http_fails_registration(string remoteUrl)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => ConfigureToken("s3cr3t", remoteUrl));

        Assert.Contains("http(s)", refusal.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/etc")]
    [InlineData(":/")]
    public void An_unusable_workflows_path_fails_registration(string workflowsPath)
    {
        var feature = new WorkflowsDesignGitReconciliationFeature { RemoteUrl = "git@example.com:acme/wf.git", WorkflowsPath = workflowsPath };

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }
}
