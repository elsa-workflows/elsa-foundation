using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Runtime.JavaScript;

public static class JavaScriptExecutionPermissions
{
    public const string Execute = "workflows.runtime.javascript.execute";
}

public sealed class JavaScriptExecutionPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Runtime.JavaScript";

    public IEnumerable<Permission> Contribute() =>
    [
        new(JavaScriptExecutionPermissions.Execute, "Execute JavaScript", "Workflows", "Execute JavaScript through the runtime API.")
    ];
}
