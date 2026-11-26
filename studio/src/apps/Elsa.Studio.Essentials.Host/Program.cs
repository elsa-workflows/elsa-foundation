using Elsa.Studio.Attention;
using Elsa.Studio.ExpressionEditors.JavaScript;
using Elsa.Studio.ExpressionEditors.Liquid;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Hosting.Distribution;
using Elsa.Studio.Secrets;
using Elsa.Studio.UserTasks;
using Elsa.Studio.Workflows;
using Elsa.Studio.Workflows.Dashboard;

// The Studio side of Elsa.Essentials.Host: the bare shell plus the modules whose backend capabilities that server advertises.
// Every other Studio module is a Nuplane add-on installed from the feed.
var builder = WebApplication.CreateBuilder(args);
builder.AddElsaStudioDistribution(
[
    typeof(FeatureManagementStudioFeature).Assembly,
    typeof(WorkflowsStudioFeature).Assembly,
    typeof(WorkflowsDashboardStudioFeature).Assembly,
    typeof(AttentionStudioFeature).Assembly,
    typeof(UserTasksStudioFeature).Assembly,
    typeof(SecretsStudioFeature).Assembly,
    typeof(JavaScriptExpressionEditorStudioFeature).Assembly,
    typeof(LiquidExpressionEditorStudioFeature).Assembly
]);

var app = builder.Build();
app.UseElsaStudioDistribution();
app.Run();
