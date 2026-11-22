namespace Elsa.Workflows.Runtime.Api.Constants;

internal static class AlterationRouteConstants
{
    internal const string Plans = "runtime/workflows/alteration-plans";
    internal const string Plan = Plans + "/{planId}";
    internal const string JobsPage = Plan + "/jobs/page";
    internal const string Job = Plan + "/jobs/{jobId}";
    internal const string Cancel = Plan + "/cancel";
}
