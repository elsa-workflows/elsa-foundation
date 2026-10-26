namespace Elsa.Studio.Hosting;

/// <summary>Registration-time choices for the Studio host. Changing installed choices requires restart.</summary>
public sealed class StudioHostingOptions
{
    /// <summary>The Studio shell used for activation, resolution and module discovery.</summary>
    public string ShellName { get; set; } = "Default";

    /// <summary>A required shell file relative to ContentRoot, or null for host-owned configuration.</summary>
    public string? ShellsFile { get; set; } = "shells.json";

    /// <summary>The fallback document within the host's static-file provider.</summary>
    public string IndexFile { get; set; } = "studio/index.html";

    /// <summary>Explicit trusted non-root paths eligible for bearer query tokens. Empty by default.</summary>
    public string[] QueryTokenPathPrefixes { get; set; } = [];
}

internal sealed record StudioHostingSettings(string ShellName, string? ShellsFile, string IndexFile, string[] QueryTokenPathPrefixes, bool AuthEnabled)
{
    public static StudioHostingSettings Create(StudioHostingOptions options, IWebHostEnvironment environment, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(options.ShellName) || options.ShellName != options.ShellName.Trim() || options.ShellName.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new ArgumentException("Studio:Hosting:ShellName must be a non-blank shell name without path separators or control characters.");
        if (options.ShellsFile is not null && string.IsNullOrWhiteSpace(options.ShellsFile))
            throw new ArgumentException("Studio:Hosting:ShellsFile must be a file path or explicit null for host-owned configuration.");
        if (string.IsNullOrWhiteSpace(options.IndexFile) || Path.IsPathRooted(options.IndexFile) || options.IndexFile.Contains('\\') || options.IndexFile.Split('/').Any(part => part is "" or "." or "..") || options.IndexFile.Any(char.IsControl))
            throw new ArgumentException("Studio:Hosting:IndexFile must name a document within the static-file provider.");
        var prefixes = options.QueryTokenPathPrefixes ?? throw new ArgumentException("Studio:Hosting:QueryTokenPathPrefixes cannot be null.");
        if (prefixes.Any(path => string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Length < 2 || path.Contains('*') || path.Contains('?') || path.Contains('#') || path.Contains('\\') || path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || path.Split('/').Any(part => part is "." or "..")))
            throw new ArgumentException("Studio:Hosting:QueryTokenPathPrefixes must contain explicit non-root paths without wildcards, query strings or traversal segments.");
        return new(options.ShellName, options.ShellsFile is null ? null : Path.GetFullPath(options.ShellsFile, environment.ContentRootPath), options.IndexFile, prefixes.ToArray(), configuration.GetValue("Studio:Auth:Enabled", false));
    }
}
