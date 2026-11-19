namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>Shared allow-list for bundled artwork selection and public asset delivery.</summary>
internal static class LoginArtwork
{
    internal const string Default = "pocket-orchestra";

    internal static bool IsKnown(string? name) => name is
        "river-to-sea" or "open-architecture" or "flowing-ribbons" or "paper-landscape" or
        "creative-studio" or "botanical-growth" or "constellation" or "miniature-world" or
        "glass-and-light" or "unfolding-map" or "pocket-orchestra" or "parcel-room" or "field-guide" or "ember" or "tidal" or "daybreak" or "canopy";

    internal static bool IsBundledFile(string? name) =>
        name is not null && name.EndsWith(".jpg", StringComparison.Ordinal) && IsKnown(name[..^4]);
}
