using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Attention.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;

namespace Elsa.Secrets.Attention;

public sealed class SecretsAttentionContributor(
    ISecretRepository repository,
    TimeProvider? timeProvider = null) : IAttentionContributor
{
    private const int MaximumItems = 5;
    private const int MaximumSoonExpiringDays = 3650;
    private const string SoonExpiringDaysThreshold = "soonExpiringDays";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AttentionContributorDescriptor Descriptor { get; } = new(
        "secrets",
        "Secrets",
        SecretsPermissions.Read,
        [
            new(
                SoonExpiringDaysThreshold,
                "Soon-expiring horizon",
                "Number of days before expiration that an active secret requires attention.",
                "30")
        ]);

    public async ValueTask<AttentionContribution> EvaluateAsync(
        AttentionContributorContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Query.TenantId))
            return Unavailable("SECRETS_ATTENTION_TENANT_UNAVAILABLE", "An authoritative tenant context is required.");

        if (!TryGetSoonExpiringHorizon(context.Thresholds, out var horizon))
            return Unavailable("SECRETS_ATTENTION_CONFIGURATION_INVALID", "The soon-expiring horizon must be a whole number of days from 0 through 3650.");

        context.Budget.ConsumeDownstreamCall();
        var page = await repository.ListPageAsync(
            context.Query.TenantId,
            new SecretRepositoryListRequest(take: SecretRepositoryListRequest.MaximumTake),
            cancellationToken);
        var secrets = page.Items;
        if (page.TotalCount > secrets.Count)
            return Unavailable("SECRETS_ATTENTION_RESULT_SET_TOO_LARGE", "The bounded secret query could not evaluate the complete tenant result set.");
        if (secrets.Any(secret => !string.Equals(secret.TenantId, context.Query.TenantId, StringComparison.Ordinal)))
            return Unavailable("SECRETS_ATTENTION_TENANT_SCOPE_INVALID", "The secret provider returned data outside the requested tenant scope.");

        var now = _timeProvider.GetUtcNow();
        var conditions = secrets
            .Select(secret => Evaluate(secret, now, horizon))
            .Where(condition => condition is not null)
            .Select(condition => condition!)
            .OrderBy(condition => condition.Severity)
            .ThenByDescending(condition => condition.OccurredAt)
            .ThenBy(condition => condition.Secret.Id, StringComparer.Ordinal)
            .ToArray();
        var items = conditions.Take(MaximumItems).Select(Map).ToArray();

        return AttentionContribution.Ready(items, conditions.Length, conditions.Length > items.Length);
    }

    private static SecretCondition? Evaluate(Secret secret, DateTimeOffset now, TimeSpan horizon)
    {
        if (secret.Status is SecretStatus.Deleted or SecretStatus.Retired)
            return null;

        var currentVersion = secret.Versions.OrderByDescending(version => version.Version).FirstOrDefault();
        if (secret.Status == SecretStatus.Revoked || currentVersion?.Status == SecretStatus.Revoked)
        {
            var occurredAt = ClampToObservation(secret.UpdatedAt ?? currentVersion?.CreatedAt ?? secret.CreatedAt, now);
            return new(secret, currentVersion, SecretConditionKind.Revoked, AttentionSeverity.Critical, occurredAt, now);
        }

        var expiresAt = currentVersion?.ExpiresAt;
        if (secret.Status == SecretStatus.Expired || currentVersion?.Status == SecretStatus.Expired || expiresAt <= now)
        {
            var occurredAt = ClampToObservation(expiresAt ?? secret.UpdatedAt ?? secret.CreatedAt, now);
            return new(secret, currentVersion, SecretConditionKind.Expired, AttentionSeverity.Critical, occurredAt, now);
        }

        if (secret.Status == SecretStatus.Active && currentVersion?.Status == SecretStatus.Active &&
            expiresAt > now && expiresAt <= now + horizon)
        {
            var occurredAt = ClampToObservation(LaterOf(expiresAt.Value - horizon, secret.CreatedAt), now);
            return new(secret, currentVersion, SecretConditionKind.SoonExpiring, AttentionSeverity.Warning, occurredAt, now);
        }

        return null;
    }

    private static AttentionItem Map(SecretCondition condition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Secret.Id);
        var title = condition.Kind switch
        {
            SecretConditionKind.Revoked => "A secret has been revoked",
            SecretConditionKind.Expired => "A secret has expired",
            _ => "A secret expires soon"
        };
        var summary = BuildSummary(condition);

        return new(
            $"secret:{condition.Secret.Id}",
            HashGeneration(condition),
            condition.Severity,
            title,
            summary,
            condition.OccurredAt,
            condition.ObservedAt,
            1,
            new("/security/secrets", "Inspect secrets"),
            [new("secret", condition.Secret.Id)],
            AttentionSensitivity.Restricted);
    }

    private static string BuildSummary(SecretCondition condition)
    {
        var label = SanitizeLabel(condition.Secret.DisplayName);
        return condition.Kind switch
        {
            SecretConditionKind.Revoked => $"{label} was revoked.",
            SecretConditionKind.Expired => $"{label} expired.",
            _ => $"{label} expires on {condition.Version!.ExpiresAt:yyyy-MM-dd}."
        };
    }

    private static string SanitizeLabel(string value)
    {
        var sanitized = new string((value ?? "").Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
            return "Secret";
        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }

    private static string HashGeneration(SecretCondition condition)
    {
        var canonical = string.Join(
            "\n",
            condition.Secret.Id,
            condition.Kind,
            condition.Version?.Version.ToString(CultureInfo.InvariantCulture) ?? "none",
            condition.OccurredAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }

    private static bool TryGetSoonExpiringHorizon(
        IReadOnlyDictionary<string, string> thresholds,
        out TimeSpan horizon)
    {
        horizon = default;
        if (!thresholds.TryGetValue(SoonExpiringDaysThreshold, out var configured) ||
            !int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ||
            days is < 0 or > MaximumSoonExpiringDays)
            return false;

        horizon = TimeSpan.FromDays(days);
        return true;
    }

    private static DateTimeOffset ClampToObservation(DateTimeOffset value, DateTimeOffset observedAt) =>
        value <= observedAt ? value : observedAt;

    private static DateTimeOffset LaterOf(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static AttentionContribution Unavailable(string errorCode, string detail) =>
        AttentionContribution.Unavailable(errorCode, detail, new("/security/secrets", "Inspect secrets"));

    private enum SecretConditionKind
    {
        Revoked,
        Expired,
        SoonExpiring
    }

    private sealed record SecretCondition(
        Secret Secret,
        SecretVersion? Version,
        SecretConditionKind Kind,
        AttentionSeverity Severity,
        DateTimeOffset OccurredAt,
        DateTimeOffset ObservedAt);
}
