namespace Elsa.Activities.Http.Options;

/// <summary>
/// Transport defaults for the outbound HTTP activity client (<c>SendHttpRequest</c>). Default-constructable
/// (init-only properties) so the standard options pipeline can materialize it and hosts can override it via
/// <c>services.Configure&lt;HttpActivityOptions&gt;(...)</c>. Applied to the named
/// <see cref="System.Net.Http.IHttpClientFactory"/> client in <c>ActivitiesHttpFeature</c>.
/// </summary>
/// <remarks>
/// A per-request <c>Timeout</c> input on the activity overrides <see cref="DefaultTimeout"/>; the timeout is
/// enforced with a linked <see cref="System.Threading.CancellationTokenSource"/> around the send rather than
/// the ambient <c>HttpClient.Timeout</c>, so it composes with the workflow's own cancellation token.
/// </remarks>
public record HttpActivityOptions
{
    /// <summary>Default request timeout applied when the activity does not specify one. Defaults to 60 seconds.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Whether the client follows redirects automatically. Defaults to <c>true</c>.</summary>
    public bool AllowAutoRedirect { get; init; } = true;

    /// <summary>Maximum number of automatic redirects to follow (bounds redirect loops). Defaults to 8.</summary>
    public int MaxAutomaticRedirections { get; init; } = 8;
}
