using System.Globalization;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Agent.Specialists.Connections;

internal sealed record SpecialistConnectionProfile(Uri Endpoint, string ModelId, int MaximumOutputTokens, bool StructuredResults, bool TemperatureSupported, string IncarnationId)
{
    internal static SpecialistConnectionProfile Read(IResolvedConnection connection, string connectionType, SpecialistModelReference? model)
    {
        if (connection.TypeName != connectionType || connection.Owner != ConnectionOwners.Tenant || connection.Status != ConnectionStatuses.Ready ||
            connection.AuthScheme != HttpAuthSchemes.ApiKey || connection.Version < 1 ||
            !Guid.TryParseExact(connection.IncarnationId, "N", out var incarnation) || incarnation == Guid.Empty ||
            !connection.Settings.TryGetValue("endpoint", out var value) || !Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/" || connection.Hosts.Kind == AllowedHostsPolicy.AnyKind || !connection.Hosts.Allows(endpoint) ||
            !connection.Settings.TryGetValue("modelId", out var modelId) || string.IsNullOrWhiteSpace(modelId) || modelId.Length > 256 ||
            modelId.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.')) || model is not null && model.ModelId != modelId ||
            !connection.Settings.TryGetValue("maximumOutputTokens", out var maximum) || !int.TryParse(maximum, NumberStyles.None, CultureInfo.InvariantCulture, out var tokens) || tokens is < 1 or > 1_000_000 ||
            !connection.Settings.TryGetValue("structuredResults", out var structured) || !bool.TryParse(structured, out var supports) ||
            !connection.Settings.TryGetValue("temperatureSupported", out var temperature) || !bool.TryParse(temperature, out var supportsTemperature))
            throw new InvalidOperationException("The model connection does not satisfy the authorized specialist profile.");
        return new(endpoint, modelId, tokens, supports, supportsTemperature, connection.IncarnationId!);
    }
}
