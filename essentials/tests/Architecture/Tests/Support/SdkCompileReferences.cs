using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Architecture.Tests;

/// <summary>Immutable witnesses for the SDK's final compiler reference images, after conflict and reference-assembly selection.</summary>
internal sealed class SdkCompileReferences
{
    internal sealed record Asset(string Path, string Identity, Guid Mvid, string Sha256, string[] Aliases, bool EmbedInteropTypes);
    private readonly Dictionary<string, Asset> images = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, Asset> Read(JsonElement evaluated)
    {
        if (!evaluated.GetProperty("Items").TryGetProperty("ReferencePathWithRefAssemblies", out var selected))
            throw new InvalidOperationException("SDK input evaluation omitted its final ReferencePathWithRefAssemblies.");
        var result = new Dictionary<string, Asset>(StringComparer.Ordinal);
        foreach (var item in selected.EnumerateArray())
        {
            var path = Path.GetFullPath(item.GetProperty("FullPath").GetString()!);
            if (!File.Exists(path)) throw new InvalidOperationException("An SDK-selected compile reference is missing: " + path);
            var image = Image(path);
            var aliases = item.TryGetProperty("Aliases", out var alias) ? alias.GetString()!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            var embedded = item.TryGetProperty("EmbedInteropTypes", out var embed) && !string.IsNullOrWhiteSpace(embed.GetString())
                ? bool.Parse(embed.GetString()!) : false;
            image = image with { Aliases = aliases, EmbedInteropTypes = embedded };
            var name = new AssemblyName(image.Identity).Name!;
            if (result.TryGetValue(name, out var prior) && (prior.Identity != image.Identity || prior.Sha256 != image.Sha256 ||
                    !prior.Aliases.SequenceEqual(image.Aliases) || prior.EmbedInteropTypes != image.EmbedInteropTypes))
                throw new InvalidOperationException("SDK ReferencePath has ambiguous assembly selections for " + name);
            result[name] = image;
        }
        if (result.Count == 0) throw new InvalidOperationException("SDK input evaluation returned an empty ReferencePath.");
        return result;
    }

    private Asset Image(string path)
    {
        if (images.TryGetValue(path, out var image)) return image;
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        var reader = pe.GetMetadataReader();
        var definition = reader.GetAssemblyDefinition();
        var name = new AssemblyName(reader.GetString(definition.Name))
        {
            Version = definition.Version,
            CultureName = definition.Culture.IsNil ? "" : reader.GetString(definition.Culture),
            ContentType = ((int)definition.Flags & 0x200) != 0 ? AssemblyContentType.WindowsRuntime : AssemblyContentType.Default
        };
        var key = reader.GetBlobBytes(definition.PublicKey);
        if (key.Length != 0) name.SetPublicKey(key);
        else name.SetPublicKeyToken([]);
        image = new(path, name.FullName!, reader.GetGuid(reader.GetModuleDefinition().Mvid), Convert.ToHexStringLower(SHA256.HashData(bytes)), [], false);
        images.Add(path, image);
        return image;
    }
}
