namespace Elsa3.Mapping.Services;

/// <summary>What an Elsa 3 typed value was inside an Elsa 3 script (spec 1223 FR-016, FR-032; data-model §3.1).</summary>
public enum Elsa3ScriptValueKind
{
    /// <summary>Converted to a JavaScript value (string, number, boolean, JSON object or array): nothing to watch.</summary>
    Json,

    /// <summary><c>DateTime</c>/<c>DateTimeOffset</c>: Elsa 3 handed scripts a JavaScript <c>Date</c> (FR-032).</summary>
    Date,

    /// <summary>A .NET <c>Guid</c>, handed over as a wrapped object.</summary>
    Guid,

    /// <summary>Another wrapped .NET object (<c>FormFile</c>, <c>Stream</c>, <c>TimeSpan</c>, <c>Uri</c>, ...).</summary>
    Object,

    /// <summary>A .NET list or dictionary, wrapped with JavaScript array or object behaviour.</summary>
    Collection,
}
