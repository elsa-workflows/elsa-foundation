using System.Security.Cryptography;

namespace Elsa.Cli.Worker;

/// <summary>Privately observes installed closure metadata and refuses drift around managed assembly loading.</summary>
/// <remarks>This is an observation, not an atomic package snapshot or proof of deployed runtime parity.</remarks>
public sealed class CandidateClosureObservation
{
    private readonly Func<string, byte[]?> fingerprint;
    private readonly Func<string, IReadOnlyList<InstalledPackage>> probe;
    private readonly Dictionary<string, byte[]?> observed = new(PathComparer);
    private IReadOnlyList<string>? probeRoots;
    private string[]? packageIdentity;

    public CandidateClosureObservation() : this(ReadFingerprint, NuplaneInstallRoot.Probe) { }

    public CandidateClosureObservation(Func<string, byte[]?> fingerprint,
        Func<string, IReadOnlyList<InstalledPackage>> probe)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(probe);
        this.fingerprint = fingerprint;
        this.probe = probe;
    }

    /// <summary>Observes content identity or absence; repeated observations must agree.</summary>
    /// <exception cref="WorkerRefusal">The selected metadata is unavailable or changed.</exception>
    public bool ObserveFile(string path, bool required)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var value = fingerprint(fullPath);
            if (required && value is null)
                throw Unavailable();
            Retain(fullPath, value);
            return value is not null;
        }
        catch (WorkerRefusal refusal)
        { throw refusal.Code == "candidate-closure-changed" ? Changed() : Unavailable(); }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        { throw Unavailable(); }
    }

    /// <summary>Retains the identity of the exact bytes used by the dependency parser.</summary>
    /// <exception cref="WorkerRefusal">The captured metadata identity is unavailable or changed.</exception>
    public void ObserveCapturedFile(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try { Retain(Path.GetFullPath(path), SHA256.HashData(content)); }
        catch (WorkerRefusal refusal)
        { throw refusal.Code == "candidate-closure-changed" ? Changed() : Unavailable(); }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        { throw Unavailable(); }
    }

    /// <summary>Observes selected package declarations and, for stateless roots, the completed install set.</summary>
    /// <exception cref="WorkerRefusal">The selected package metadata is unavailable or changed.</exception>
    public void ObservePackages(IReadOnlyList<InstalledPackage> packages, IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(roots);
        try
        {
            var identities = Identities(packages);
            if (packageIdentity is not null)
                throw Unavailable();
            packageIdentity = identities;
            probeRoots = roots.Select(Path.GetFullPath).ToArray();
            foreach (var package in packages)
            {
                foreach (var name in new[] { NuplaneInstallRoot.ReadyMarker, "nuplane.json", "elsa-package.json", "build/elsa-package.json" })
                    ObserveFile(Path.Join(package.InstallPath, name), required: false);
            }
        }
        catch (WorkerRefusal refusal)
        { throw refusal.Code == "candidate-closure-changed" ? Changed() : Unavailable(); }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        { throw Unavailable(); }
    }

    /// <summary>Checks every observed presence/content and completed package selection before payload dispatch.</summary>
    /// <exception cref="WorkerRefusal">Observed closure metadata changed or could not be rechecked.</exception>
    public void VerifyUnchanged()
    {
        try
        {
            foreach (var (path, expected) in observed)
            {
                var actual = fingerprint(path);
                if (expected is null ? actual is not null : actual is null || !expected.AsSpan().SequenceEqual(actual))
                    throw Changed();
            }
            if (probeRoots is { Count: > 0 } && packageIdentity is not null &&
                !packageIdentity.SequenceEqual(Identities(probeRoots.SelectMany(root => probe(root)).ToArray()), StringComparer.Ordinal))
                throw Changed();
        }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        { throw Changed(); }
    }

    private void Retain(string path, byte[]? value)
    {
        if (observed.TryGetValue(path, out var previous))
        {
            if (previous is null ? value is not null : value is null || !previous.AsSpan().SequenceEqual(value))
                throw Changed();
            return;
        }
        observed.Add(path, value?.ToArray());
    }

    private static string[] Identities(IReadOnlyList<InstalledPackage> packages)
    {
        if (packages.Any(package => package is null || string.IsNullOrWhiteSpace(package.Id) ||
                                    string.IsNullOrWhiteSpace(package.Version) || string.IsNullOrWhiteSpace(package.InstallPath) ||
                                    package.FeedName is null) ||
            packages.Select(package => package.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Count)
            throw Unavailable();
        // This private projection is never serialized. Each component is length framed to prevent delimiter aliases.
        return packages.Select(package => string.Concat(new[] { package.Id, package.Version,
                    Path.GetFullPath(package.InstallPath), package.FeedName,
                    package.InstalledAtUtc.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }.Select(value => $"{value.Length}:{value}")))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static byte[]? ReadFingerprint(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw Unavailable();
        using var input = File.OpenRead(path);
        return SHA256.HashData(input);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static WorkerRefusal Changed() => WorkerRefusal.Resolution("candidate-closure-changed", "The selected installed host closure changed during inspection.");
    private static WorkerRefusal Unavailable() => WorkerRefusal.Resolution("candidate-host-unavailable", "The selected installed host closure could not be inspected.");
}
