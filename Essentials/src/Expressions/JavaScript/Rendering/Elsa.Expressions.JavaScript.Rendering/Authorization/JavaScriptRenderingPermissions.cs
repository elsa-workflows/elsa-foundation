using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Expressions.JavaScript.Rendering;

internal static class JavaScriptRenderingPermissions
{
    public const string Render = "expressions.javascript.render";
}

internal sealed class JavaScriptRenderingPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Expressions.JavaScript.Rendering";

    public IEnumerable<Permission> Contribute() =>
    [
        new(JavaScriptRenderingPermissions.Render, "Render JavaScript declarations", "Expressions", "Render the JavaScript declaration document used by design-time clients.")
    ];
}
