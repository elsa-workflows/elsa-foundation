namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// A single logical scope active when an entry was captured. Keyed scope state is exposed as
/// <see cref="Items"/>; non-keyed scopes are rendered into <see cref="Text"/>.
/// </summary>
public sealed record LogScope(IReadOnlyList<LogProperty> Items, string? Text = null);
