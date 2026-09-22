using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// The engine a package-hosted host selects with <c>Nuplane:Capabilities:ef-provider</c> (spec 172 D3), read
/// out of the same layered <c>appsettings.json</c> <c>--restore</c> hands to Nuplane.
/// </summary>
/// <remarks>
/// <para>
/// The key is Nuplane's, not Elsa's: every EF module package declares the <c>ef-provider</c> capability in
/// its <c>nuplane.json</c>, and Nuplane turns the host's selection into a root package. This tool reads the
/// same key for one purpose only — keeping <c>--provider</c> authoritative against it (ADR 0076 D4). It
/// never acts on the selection, and never lets it decide a provider.
/// </para>
/// <para>
/// <see cref="Read"/> names a configuration type, so it follows the same isolation rule as
/// <see cref="HostAppSettings.Read"/>: <see cref="MethodImplOptions.NoInlining"/>, reached only once
/// <see cref="Find"/> has decided — without naming one — that this host is one that could carry a selection
/// at all.
/// </para>
/// </remarks>
public static class HostCapabilitySelection
{
    /// <summary>The capability every EF module package declares (spec 172 FR-001).</summary>
    public const string CapabilityName = "ef-provider";

    /// <summary>
    /// The configuration key, spelled exactly as <c>EfRelationalProviderBinding.CapabilitySelectionKey</c>
    /// spells it. Repeated rather than shared because this assembly references no persistence assembly of
    /// its own (ADR 0076 D1); the two are compared by
    /// <c>ToolingEntryPointTests.The_worker_and_the_persistence_assembly_name_the_same_capability_key</c>.
    /// </summary>
    public const string Key = $"Nuplane:Capabilities:{CapabilityName}";

    /// <summary>
    /// The option name(s) this host selects, or <c>null</c> when it selects none. Names no configuration
    /// type, so any caller may ask.
    /// </summary>
    /// <remarks>
    /// Gated on the host pinning Nuplane at all: the key only means something for a host whose modules and
    /// engine arrive as packages, and a host with no Nuplane carries none of the configuration assemblies
    /// <see cref="Read"/> resolves out of the host's own closure.
    /// </remarks>
    public static IReadOnlyList<string>? Find(WorkerRequest request, HostDepsFile deps)
    {
        if (deps.ForPackage(HostPackageRestore.NuplanePackageId) is null)
            return null;

        var hostDirectory = request.HostDirectory!;
        var environment = request.Environment!;
        if (!HostAppSettings.Exist(hostDirectory, environment))
            return null;

        return Guarded(hostDirectory, environment);
    }

    /// <summary>
    /// Refuses rather than guessing when the host's configuration cannot be read at all. Skipping the check
    /// would be the one failure that looks like success: a run that scripts one dialect for a host whose
    /// closure carries another, with nothing on either stream to say the comparison never happened.
    /// </summary>
    private static IReadOnlyList<string>? Guarded(string hostDirectory, string environment)
    {
        try
        {
            return Read(hostDirectory, environment);
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or MissingMethodException or MissingMemberException)
        {
            throw WorkerRefusal.Resolution(
                "capability-selection-unreadable",
                $"This host pins Nuplane and has an '{HostAppSettings.BaseFileName}', so it may select its provider engine " +
                $"with '{Key}' — and that file could not be read, because this host does not provide all of the " +
                $"configuration assemblies it takes: {failure.Message} " +
                $"Without it '--provider' cannot be checked against the selection, so nothing is scripted from it.");
        }
        catch (Exception failure) when (failure is InvalidOperationException or IOException or System.Text.Json.JsonException or FormatException)
        {
            throw WorkerRefusal.Resolution(
                "capability-selection-unreadable",
                $"This host's own '{HostAppSettings.BaseFileName}' could not be read, so '{Key}' could not be compared " +
                $"with '--provider': {failure.GetType().Name}: {failure.Message} Nothing is scripted from it.");
        }
    }

    /// <summary>
    /// The selection exactly as Nuplane's own <c>CapabilitySelectionConfigurationReader</c> reads it: the
    /// section's value, or its <c>Option</c> child, split on commas and trimmed. A section that sets both to
    /// different values, or resolves to no option at all, is a shape Nuplane refuses at its own options
    /// validation — so it is refused here too rather than read as "no selection", which would silently skip
    /// the agreement check for a host that plainly meant to make one.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<string>? Read(string hostDirectory, string environment)
    {
        var section = HostAppSettings.Read(hostDirectory, environment).GetSection(Key);
        var value = section.Value;
        var option = section["Option"];
        if (!section.Exists() && value is null && option is null)
            return null;

        if (value is not null && option is not null && !string.Equals(value, option, StringComparison.Ordinal))
        {
            throw WorkerRefusal.Resolution(
                "capability-selection-invalid",
                $"'{Key}' sets both a value ('{value}') and an Option child ('{option}'); set only one. " +
                "Nuplane refuses this host's configuration for the same reason, so the engine its closure would " +
                "carry is undecided and nothing is scripted against it.");
        }

        var options = (option ?? value)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (options.Length > 0)
            return options;

        throw WorkerRefusal.Resolution(
            "capability-selection-invalid",
            $"'{Key}' is present but selects no option. Nuplane reads it as a string — one option name, or several " +
            "separated by commas — or as an object with an 'Option' child of that shape; a JSON array is neither, " +
            "and Nuplane refuses this host's configuration for it. Nothing is scripted against an undecided engine.");
    }
}
