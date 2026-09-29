using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nuplane;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;

namespace Elsa.Testing;

/// <summary>
/// A host's <c>Nuplane:Loading:SharedAssemblies</c> as Nuplane itself holds them (#2150): bound by the
/// <c>AutoloadPackages</c> call both hosts' <c>Program.cs</c> make, validated by Nuplane's own options validation, and
/// matched by Nuplane's own <see cref="SharedAssemblyPolicyMatcher"/>, which decides whether a package's reference to an
/// assembly resolves to the host's copy. Reading the names alone would take an entry that never matches, or never binds,
/// for a share.
/// </summary>
internal sealed class NuplaneSharedAssemblyPolicy
{
    private static readonly SharedAssemblyPolicyMatcher Matcher = new();

    private NuplaneSharedAssemblyPolicy(IReadOnlyList<SharedAssemblyPolicyEntry> entries) => Entries = entries;

    /// <summary>The entries exactly as Nuplane's loader receives them.</summary>
    public IReadOnlyList<SharedAssemblyPolicyEntry> Entries { get; }

    /// <summary>
    /// Binds the <c>Loading</c> section of <paramref name="nuplane"/>, a host's <c>Nuplane</c> section, as the host does.
    /// Throws <see cref="OptionsValidationException"/> where the host would refuse to start: an entry that does not bind,
    /// or names a malformed public key token.
    /// </summary>
    public static NuplaneSharedAssemblyPolicy Bind(IConfiguration nuplane)
    {
        using var services = new ServiceCollection()
            .AddNuplane(builder => builder.AutoloadPackages(nuplane.GetSection("Loading")))
            .BuildServiceProvider();

        return new([
            .. services.GetRequiredService<IOptions<LoadingOptions>>().Value.SharedAssemblies
                .Select(identity => new SharedAssemblyPolicyEntry(identity.Name, identity.PublicKeyToken, identity.MajorVersion))
        ]);
    }

    /// <summary>Whether Nuplane resolves a package's reference to <paramref name="requested"/> to the host's copy.</summary>
    public bool Shares(AssemblyName requested) => Matcher.IsMatch(requested, Entries);

    /// <summary>This policy without the entries for <paramref name="names"/>, for the direction a missing share takes.</summary>
    public NuplaneSharedAssemblyPolicy Without(IEnumerable<string> names)
    {
        var withheld = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new([.. Entries.Where(entry => !withheld.Contains(entry.Name))]);
    }
}
