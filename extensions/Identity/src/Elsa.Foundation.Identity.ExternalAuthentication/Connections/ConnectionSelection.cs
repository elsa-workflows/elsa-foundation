using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal static partial class ConnectionSelection
{
    public const int MaximumRecords = 256;

    public static ConnectionRegistryView Select(ExternalAuthenticationOptions options, ConnectionCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(options.TenantId) || options.TenantId.Length > 400 || options.Connections is null ||
            options.AllowedOverrideKeys is null || options.AllowedRoleIds is null)
            throw new InvalidOperationException("External authentication requires a host tenant and valid deployment policy collections.");
        var deploymentRecords = options.Connections.Select(record => record is null
            ? throw new InvalidOperationException("A deployment connection cannot be null.")
            : record with { Id = "deployment:" + record.Key }).ToArray();
        Validate(deploymentRecords);
        var baseline = deploymentRecords.Select(Snapshot).ToArray();
        Validate(catalog.Records);
        var database = options.AllowDatabaseManagement ? catalog.Records.Select(Snapshot).ToArray() : [];
        var views = new List<ConnectionView>();
        foreach (var key in baseline.Concat(database).Select(record => record.Key).Distinct(StringComparer.Ordinal))
        {
            var deployment = baseline.SingleOrDefault(record => record.Key == key);
            var stored = database.SingleOrDefault(record => record.Key == key);
            var permitted = deployment is not null && options.AllowDeploymentOverrides &&
                (options.AllowedOverrideKeys.Count == 0 || options.AllowedOverrideKeys.Contains(key, StringComparer.Ordinal));
            var selected = deployment is null ? stored : stored is { OverridesDeployment: true, ArchivedAt: null } && permitted ? stored : deployment;
            if (deployment is not null) Add(deployment, ConnectionSource.Deployment);
            if (stored is not null) Add(stored, ConnectionSource.Database);

            void Add(ConnectionRecord record, ConnectionSource source) => views.Add(new ConnectionView(
                record, source, ReferenceEquals(record, selected) && record.ArchivedAt is null,
                !ReferenceEquals(record, selected) && record.ArchivedAt is null ? selected?.Id : null,
                permitted, MaterialRevision(record)));
        }
        return new ConnectionRegistryView(views.OrderBy(view => view.Record.DisplayOrder)
            .ThenBy(view => view.Record.Key, StringComparer.Ordinal).ThenBy(view => view.Source).ToArray(),
            catalog.Revision, options.AllowDatabaseManagement);
    }

    public static void Validate(IReadOnlyList<ConnectionRecord> records)
    {
        if (records is null || records.Count > MaximumRecords)
            throw new InvalidOperationException("The external connection catalog is missing or exceeds its bound.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record is null || string.IsNullOrWhiteSpace(record.Id) || record.Id.Length > 128 ||
                record.Key is null || !LogicalKey().IsMatch(record.Key) ||
                !ids.Add(record.Id) || !keys.Add(record.Key) ||
                record.Settings is null || record.Secrets is null || record.Admission is null ||
                record.Admission.DefaultRoleIds is null ||
                record.Settings.Count > 64 || record.Secrets.Count > 16 ||
                record.Admission.DefaultRoleIds.Count > 256 ||
                string.IsNullOrWhiteSpace(record.Protocol) || record.Protocol.Length > 80 ||
                string.IsNullOrWhiteSpace(record.Admission.Type) || record.Admission.Type.Length > 80 || record.Admission.Matcher?.Length > 80 ||
                record.DisplayName is null || record.DisplayName.Length > 256)
                throw new InvalidOperationException("The external connection catalog has an invalid or duplicate identity or document.");
            foreach (var setting in record.Settings)
                if (string.IsNullOrWhiteSpace(setting.Key) || setting.Key.Length > 80 || setting.Value is null || setting.Value.Length > 8192 ||
                    CredentialField(setting.Key))
                    throw new InvalidOperationException("Connection settings cannot contain inline credentials or invalid fields; use secret references.");
            if (record.Protocol == "oidc" && record.Settings.TryGetValue("authority", out var authority) &&
                Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.UserInfo.Length != 0)
                throw new InvalidOperationException("Issuer URLs cannot contain inline credentials; use secret references.");
            foreach (var binding in record.Secrets)
                if (string.IsNullOrWhiteSpace(binding.Key) || binding.Key.Length > 80 || binding.Value is null ||
                    string.IsNullOrWhiteSpace(binding.Value.Name) || binding.Value.Name.Length > 256 ||
                    binding.Value.TypeName?.Length > 128 || binding.Value.Scope?.Length > 256)
                    throw new InvalidOperationException("The connection has an invalid secret reference.");
            if (record.Admission.DefaultRoleIds.Any(role => string.IsNullOrWhiteSpace(role) || role.Length > 400))
                throw new InvalidOperationException("The connection has invalid role references.");
        }
    }

    public static ConnectionRecord Snapshot(ConnectionRecord record) => record with
    {
        Settings = new SortedDictionary<string, string>(record.Settings.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
        Secrets = new SortedDictionary<string, Elsa.Secrets.Core.Models.SecretReference>(record.Secrets.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
        Admission = record.Admission with { DefaultRoleIds = record.Admission.DefaultRoleIds.Order(StringComparer.Ordinal).ToArray() }
    };

    internal static string MaterialRevision(ConnectionRecord record)
    {
        // Presentation is not authentication material. Observations live outside the connection document.
        var material = new { record.Key, record.Protocol, record.Settings, record.Secrets, record.Admission,
            record.Enabled, record.OverridesDeployment, record.ArchivedAt };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(material)));
    }

    private static bool CredentialField(string key) => new string(key.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant()
        is "clientsecret" or "password" or "accesstoken" or "refreshtoken";

    [GeneratedRegex("\\A[a-z0-9][a-z0-9._-]{0,79}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalKey();
}
