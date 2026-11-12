namespace Elsa.Studio.Hosting.Themes;

/// <summary>Sources whose enabled, published themes may be selected; management inventory remains complete.</summary>
public enum StudioThemeSourceMode
{
    /// <summary>Only built-in themes may be selected.</summary>
    BuiltInsOnly = 0,
    /// <summary>Only definitions supplied by the configured directory source may be selected.</summary>
    DirectoryOnly = 1,
    /// <summary>Built-ins, directory definitions and managed custom themes may be selected.</summary>
    AddToBuiltIns = 2
}
