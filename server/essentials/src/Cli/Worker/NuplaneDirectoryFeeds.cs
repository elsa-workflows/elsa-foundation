using Microsoft.Extensions.Configuration;
using Nuplane.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// Registers directory-backed feeds for legacy hosts that carry <c>Nuplane.Sources.Directory</c>.
/// </summary>
/// <remarks>
/// <para>
/// Older versions of <c>AddNuplane</c> delegate directory feeds to a separate module. The worker
/// registers them through <c>NuplaneRestoreOptions.ConfigureBuilder</c> and passes the
/// callback's <em>own</em> second argument, the already-resolved <c>Nuplane</c> section, rather than a
/// configuration root captured from the call site, which would make the registration helper find no keys
/// at all.
/// </para>
/// <para>
/// The isolation rule is <see cref="NuplaneLoader"/>'s: the runtime resolves a method's types when it
/// compiles that method, so a host that carries no <c>Nuplane.Sources.Directory</c> must never reach this
/// body. <see cref="NuplaneRestoreRunner"/> therefore assigns this method as the callback only when the
/// host's own dependency file lists that package, and the method is
/// <see cref="MethodImplOptions.NoInlining"/> so it cannot be folded into the method that decides.
/// </para>
/// </remarks>
internal static class NuplaneDirectoryFeeds
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(NuplaneBuilder builder, IConfiguration nuplaneSection) =>
        builder.AddDirectoryFeedsFromConfiguration(nuplaneSection);
}
