using Microsoft.Extensions.Configuration;
using Nuplane.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// The one call that registers a host's directory-backed feeds for a restore, and the only file in this
/// project that names a type from <c>Nuplane.Sources.Directory</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nuplane's core <c>AddNuplane</c> skips every configured feed that declares a directory path, because
/// directory feeds belong to a module the core package does not reference. A caller that references that
/// module adds them back through <c>NuplaneRestoreOptions.ConfigureBuilder</c> — and must pass the
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
