namespace Elsa3.Mapping.Conversion;

/// <summary>
/// How the converter reads Elsa 3 local date and time, which Elsa 3 evaluated in its server's time zone (spec 1223
/// FR-034, ADR 0046 Dates). It is the single date setting of ADR 0046: this converter applies it to scripts; S-9 (spec
/// 1207) applies the same setting at the JSON to .NET boundary, so a host sets it once.
/// </summary>
public enum Elsa3DateInterpretation
{
    /// <summary>Nothing is assumed: zone-dependent date usage is refused with guidance. The default.</summary>
    Refuse,

    /// <summary>Convert as if the Elsa 3 server ran on UTC, with a behaviour warning on each affected expression.</summary>
    AssumeUtc,

    /// <summary>Convert with <see cref="Elsa3ConversionOptions.SourceTimeZone"/> written explicitly into the converted code.</summary>
    SourceTimeZone,
}

/// <summary>The Elsa 3 converter's settings (the <c>Elsa3Mapping</c> feature).</summary>
public sealed class Elsa3ConversionOptions
{
    /// <summary>How Elsa 3 local date and time is read (FR-034). Default <see cref="Elsa3DateInterpretation.Refuse"/>.</summary>
    public Elsa3DateInterpretation DateInterpretation { get; set; } = Elsa3DateInterpretation.Refuse;

    /// <summary>
    /// The IANA time zone of the Elsa 3 server (for example <c>Europe/Amsterdam</c>); required by, and only used with,
    /// <see cref="Elsa3DateInterpretation.SourceTimeZone"/>.
    /// </summary>
    public string? SourceTimeZone { get; set; }

    /// <summary>
    /// Checks the setting: <see cref="Elsa3DateInterpretation.SourceTimeZone"/> needs an IANA time zone ID the host
    /// knows, and the other values take none.
    /// </summary>
    public void Validate()
    {
        if (DateInterpretation != Elsa3DateInterpretation.SourceTimeZone)
        {
            if (!string.IsNullOrWhiteSpace(SourceTimeZone))
                throw new InvalidOperationException(
                    $"The Elsa 3 converter's source time zone '{SourceTimeZone}' is set, but the date interpretation is {DateInterpretation}. Set the date interpretation to SourceTimeZone, or clear the source time zone.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceTimeZone))
            throw new InvalidOperationException(
                "The Elsa 3 converter's date interpretation is SourceTimeZone, but no source time zone is set. Set it to the IANA time zone of the Elsa 3 server, for example 'Europe/Amsterdam'.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(SourceTimeZone, out var zone) || !zone.HasIanaId)
            throw new InvalidOperationException(
                $"The Elsa 3 converter's source time zone '{SourceTimeZone}' is not an IANA time zone ID this host knows. Use an IANA ID such as 'Europe/Amsterdam'.");
    }
}
