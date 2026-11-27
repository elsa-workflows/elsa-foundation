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

    /// <summary>
    /// The policy of the host whose settings are <paramref name="appSettingsPaths"/>, each layered over the one before as the
    /// host's own <c>appsettings.json</c> and its environment's <c>appsettings.{Environment}.json</c> are, bound from the
    /// merged <c>Nuplane</c> section by <see cref="Bind"/>. The JSON provider is the host's, so the <c>//</c> comments
    /// <c>Elsa.Foundation.Host</c> carries parse as they do at runtime.
    /// </summary>
    public static NuplaneSharedAssemblyPolicy FromHostAppSettings(params string[] appSettingsPaths)
    {
        var settings = new ConfigurationBuilder();
        foreach (var path in appSettingsPaths)
            settings.AddJsonFile(path);

        return Bind(settings.Build().GetSection("Nuplane"));
    }

    /// <summary>Whether Nuplane resolves a package's reference to <paramref name="requested"/> to the host's copy.</summary>
    public bool Shares(AssemblyName requested) => Matcher.IsMatch(requested, Entries);

    /// <summary>This policy without the entries for <paramref name="names"/>, withholding a share to prove the module then binds its own copy.</summary>
    public NuplaneSharedAssemblyPolicy Without(IEnumerable<string> names)
    {
        var withheld = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new([.. Entries.Where(entry => !withheld.Contains(entry.Name))]);
    }
}
