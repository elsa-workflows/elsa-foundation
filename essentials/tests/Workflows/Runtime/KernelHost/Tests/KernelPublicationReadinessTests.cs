using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelPublicationReadinessTests
{
    [Theory]
    [InlineData(false, "Missing", "Missing", "activity.runtime.consumer-missing", 3)]
    [InlineData(true, "UnsupportedSchema", "Available", "activity.runtime.consumer-schema-unsupported", 2)]
    public void Publishing_reports_activity_output_support_only_for_selected_Kernel_Hosting(bool selectKernelHosting,
        string otherSchemaStatus, string activityOutputStatus, string diagnosticCode, int diagnosticCount)
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        if (selectKernelHosting)
            new WorkflowsRuntimeKernelHostingFeature
            {
                SqlitePath = Path.Combine(Path.GetTempPath(), $"kernel-readiness-{Guid.NewGuid():N}.db")
            }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var readiness = scope.ServiceProvider.GetRequiredService<IActivityPublicationReadiness>();
        RuntimeRequirement[] requirements =
        [
            new(WellKnownRuntimeActivityConsumers.Intrinsic, RuntimeActivityDescriptor.InitialSchemaVersion),
            new(WellKnownRuntimeActivityConsumers.Intrinsic, WorkflowIntrinsicDescriptorSchemas.ActivityOutput),
            new(WellKnownRuntimeActivityConsumers.Intrinsic, "unknown")
        ];

        var statuses = readiness.RuntimeReadiness(requirements).Select(item => item.Status);
        var diagnostics = readiness.ReadinessDiagnostics(new("ActivityDraft", "draft"), requirements, []);
        Assert.Equal([otherSchemaStatus, activityOutputStatus, otherSchemaStatus], statuses);
        Assert.Equal(Enumerable.Repeat(diagnosticCode, diagnosticCount), diagnostics.Select(diagnostic => diagnostic.Code));
    }
}
