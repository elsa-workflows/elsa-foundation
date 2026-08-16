namespace Elsa.Http.Options;

/// <summary>
/// Options for the per-shell HTTP <see cref="Services.RouteTable"/>. Carries a shell discriminator so the route
/// table's <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> key is namespaced per shell (spec 089
/// follow-up, issue #592 item 5).
/// </summary>
/// <remarks>
/// Route-table isolation previously relied on <c>IMemoryCache</c> being per-shell (true today in CShells). Should
/// a future root-promoted cache be shared across shells, an un-namespaced key would alias two shells' route
/// tables onto one entry. The discriminator (the shell settings id, bound by <c>HttpFeature</c>) makes the key
/// unique per shell so the tables never collide even on a shared cache.
/// </remarks>
public sealed class RouteTableOptions
{
    /// <summary>A value unique to the owning shell (its settings id). Empty when unset (single-shell/test hosts).</summary>
    public string ShellDiscriminator { get; set; } = string.Empty;

    /// <summary>The stable owner id stamped onto workflow-authored dynamic routes.</summary>
    public string OwnerId { get; set; } = "Elsa.Http";
}
