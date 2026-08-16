namespace Elsa.Http.Options;

/// <summary>
/// Options for the per-shell HTTP <see cref="Services.RouteTable"/>. These values identify the dynamic-shell owner
/// stamped onto published route diagnostics; the child provider's shell-lifetime state owns route authority.
/// </summary>
/// <remarks>
/// <see cref="Services.RouteTableState"/> is registered once per child shell provider and supplies synchronization
/// and the current generation to scoped route-table facades. The public route-table constructor still accepts
/// <c>IMemoryCache</c> for source compatibility, but cache keys and process-global shell dictionaries do not
/// participate in isolation or correctness.
/// </remarks>
public sealed class RouteTableOptions
{
    /// <summary>
    /// The shell identity stamped onto dynamic-route ownership metadata; empty uses the compatibility identity
    /// <c>default</c>.
    /// </summary>
    public string ShellDiscriminator { get; set; } = string.Empty;

    /// <summary>The stable owner id stamped onto workflow-authored dynamic routes.</summary>
    public string OwnerId { get; set; } = "Elsa.Http";
}
