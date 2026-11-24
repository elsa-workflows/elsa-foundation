using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class S2StartupNullPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_reconciliation_records_and_pins_the_explicit_converter_option(bool enabled)
    {
        var folder = Directory.CreateTempSubdirectory("elsa3-s2-policies-");
        try
        {
            File.Copy(Path.Join(AppContext.BaseDirectory, "Fixtures", "null-members-script.json"), Path.Join(folder.FullName, "workflow.json"));
            await using var host = await Elsa3ImportHost.StartAsync(reconcileFolder: folder.FullName, configureServices: services =>
                services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
            var publication = (await host.ActivePublicationAsync("null-members-script"))!.Value;
            var version = await host.ReadAsync(services => services.GetRequiredService<IWorkflowDefinitionVersionStore>().GetAsync(publication.Workflow.VersionId));
            Assert.Equal(enabled, version.State!.StrategyOptions?.NullHandling?.DefaultOnNull == true);
            Assert.Equal(enabled, version.State.StrategyOptions?.NullHandling?.UndefinedAsNull == true);
            var executable = await host.ReadAsync(async services => await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(publication.Workflow.ArtifactId));
            Assert.NotNull(executable); Assert.Equal(enabled, executable.ValuePolicies.DefaultOnNull); Assert.Equal(enabled, executable.ValuePolicies.UndefinedAsNull);
            var run = await host.RunAsync(publication.Workflow); Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow!.Status);
        }
        finally { folder.Delete(recursive: true); }
    }
}
