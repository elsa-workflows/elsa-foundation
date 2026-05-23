using CShells.Features;
using Elsa.FastEndpoints;

namespace Elsa.Workflows.Runtime.JavaScript
{
    [ShellFeature(
       name: "JavaScriptEndpoints",
       DisplayName = "JavaScript FastEndpoints"
    )]
    public sealed class JavaScriptActivitiesEndpointsFeature : FastEndpointsFeatureBase
    {
    }
}
