namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>Host-owned login presentation, configured under <c>Identity:Login</c>. Contains no authentication settings.</summary>
public sealed class LoginPageOptions
{
    public string ProductName { get; set; } = "Elsa Studio";
    public string Tagline { get; set; } = "Space for your next great idea.";
    public string Description { get; set; } = "Bring your ideas to life, one thoughtful workflow at a time.";
    public string? LogoUrl { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>
    /// Bundled artwork: river-to-sea, open-architecture, flowing-ribbons, paper-landscape, creative-studio,
    /// botanical-growth, constellation, miniature-world, glass-and-light, unfolding-map, parcel-room or pocket-orchestra (default for illustrated layouts).
    /// The existing ember, tidal, daybreak and canopy choices remain available; field-guide provides the
    /// brochure’s pencil/watercolor capability plate. ImageUrl takes precedence in illustrated layouts.
    /// </summary>
    public string Artwork { get; set; } = LoginArtwork.Default;
    /// <summary>orbital (default), parcel-room, split, reverse, immersive, editorial or field-guide. Illustrated layouts put the form first on mobile.</summary>
    public string Layout { get; set; } = "orbital";
    /// <summary>pocket-orchestra (default for illustrated layouts), parcel-room, linen, tidal, daybreak, canopy or field-guide. Orbital uses its own dark palette.</summary>
    public string Palette { get; set; } = "pocket-orchestra";
    /// <summary>Horizontal/vertical focal coordinates from 0 to 100 for illustrated layouts.</summary>
    public double FocalX { get; set; } = 50;
    public double FocalY { get; set; } = 50;
    public string? HelpUrl { get; set; }
    public string? PrivacyUrl { get; set; }
    /// <summary>Optional single replacement for the entire HTML document. Keep the supplied FormHtml to preserve the standard fields/security context. Host code owns output encoding.</summary>
    public Func<LoginPageContext, string>? Renderer { get; set; }
}

/// <summary>Presentation context supplied after return URL validation and antiforgery issuance. FormHtml is trusted server-rendered markup.</summary>
public sealed record LoginPageContext(string ProductName, string ReturnUrl, bool ShowError, string? AntiforgeryToken, string FormHtml);
