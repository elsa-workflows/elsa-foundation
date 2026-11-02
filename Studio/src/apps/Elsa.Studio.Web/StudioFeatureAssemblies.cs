using CShells.DependencyInjection;
using Elsa.Studio.Hosting;
using Elsa.Studio.Api.Features;
using Elsa.Studio.Attention;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Diagnostics.OpenTelemetry;
using Elsa.Studio.Diagnostics.StructuredLogs;
using Elsa.Studio.ExpressionEditors.JavaScript;
using Elsa.Studio.ExpressionEditors.Liquid;
using Elsa.Studio.ExtensionBuilder;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Connections;
using Elsa.Studio.Etl;
using Elsa.Studio.Secrets;
using Elsa.Studio.Agent.Specialists;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Workflows;
using Elsa.Studio.Workflows.Dashboard;

using Elsa.Studio.Backends;

namespace Elsa.Studio.Web;

internal static class StudioFeatureAssemblies
{
    /// <summary>Chooses the in-box modules shipped by this Studio distribution.</summary>
    public static CShellsBuilder WithStudioFeatureAssemblies(this CShellsBuilder shells)
    {
        return shells.WithStudioStartupAssemblies(
                typeof(StudioApiFeature).Assembly,
                typeof(BackendsStudioFeature).Assembly,
                typeof(ConsoleStreamStudioFeature).Assembly,
                typeof(DiagnosticsOpenTelemetryStudioFeature).Assembly,
                typeof(DiagnosticsStructuredLogsStudioFeature).Assembly,
                typeof(ExtensionBuilderStudioFeature).Assembly,
                typeof(FeatureManagementStudioFeature).Assembly,
                typeof(DashboardStudioFeature).Assembly,
                typeof(AttentionStudioFeature).Assembly,
                typeof(WorkflowsDashboardStudioFeature).Assembly,
                typeof(WeaverWorkflowsStudioFeature).Assembly,
                typeof(WorkflowsStudioFeature).Assembly,
                typeof(SecretsStudioFeature).Assembly,
                typeof(AgentSpecialistsStudioFeature).Assembly,
                typeof(ConnectionsStudioFeature).Assembly,
                typeof(EtlStudioFeature).Assembly,
                typeof(JavaScriptExpressionEditorStudioFeature).Assembly,
                typeof(LiquidExpressionEditorStudioFeature).Assembly);
    }
}
