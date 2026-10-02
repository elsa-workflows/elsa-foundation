using CShells.DependencyInjection;
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
using Elsa.Studio.Secrets;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Workflows;
using Elsa.Studio.Workflows.Dashboard;

namespace Elsa.Studio.Web;

internal static class StudioFeatureAssemblies
{
    /// <summary>
    /// Registers the assemblies CShells discovers Studio features in: installed Nuplane packages plus the in-box modules.
    /// Registering explicit providers turns off CShells' host-assembly scanning, so a feature enabled in shells.json whose
    /// assembly is missing here is skipped at startup with only a warning.
    /// </summary>
    public static CShellsBuilder WithStudioFeatureAssemblies(this CShellsBuilder shells) =>
        shells
            .WithAssemblyProvider<StudioNuplaneAssemblyProvider>()
            .WithAssemblies(
                typeof(StudioApiFeature).Assembly,
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
                typeof(JavaScriptExpressionEditorStudioFeature).Assembly,
                typeof(LiquidExpressionEditorStudioFeature).Assembly,
                typeof(ThemeStoreCoreStudioFeature).Assembly);
}
