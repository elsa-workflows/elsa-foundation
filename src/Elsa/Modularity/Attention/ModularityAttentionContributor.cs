using System.Security.Cryptography;
using System.Text;
using Elsa.Attention.Core;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Attention;

public sealed class ModularityAttentionContributor(
    IFeatureManagementService featureManagementService,
    TimeProvider? timeProvider = null) : IAttentionContributor
{
    private const int MaximumItems = 5;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AttentionContributorDescriptor Descriptor { get; } = new(
        "modularity",
        "Module management",
        ModuleManagementPermissionKeys.Read);

    public async ValueTask<AttentionContribution> EvaluateAsync(
        AttentionContributorContext context,
        CancellationToken cancellationToken = default)
    {
        context.Budget.ConsumeDownstreamCall();
        var catalog = await featureManagementService.GetCatalogAsync(cancellationToken);
        var byId = catalog.Features.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var observedAt = _timeProvider.GetUtcNow();

        var conditions = catalog.Features
            .Select(feature => Evaluate(feature, byId, observedAt))
            .Where(condition => condition is not null)
            .Select(condition => condition!)
            .OrderBy(item => item.Severity)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var items = conditions.Take(MaximumItems).ToArray();

        return AttentionContribution.Ready(items, conditions.Length, conditions.Length > items.Length);
    }

    private static AttentionItem? Evaluate(
        FeatureCatalogItem feature,
        IReadOnlyDictionary<string, FeatureCatalogItem> catalog,
        DateTimeOffset observedAt)
    {
        var hasReadError = !string.IsNullOrWhiteSpace(feature.ReadError);
        var dependencyIssues = feature.Enabled
            ? feature.Dependencies
                .Where(dependency => !dependency.Optional)
                .Select(dependency => (Dependency: dependency, Found: catalog.GetValueOrDefault(dependency.Id)))
                .Where(result => result.Found is null || !result.Found.Enabled)
                .ToArray()
            : [];

        if (!hasReadError && dependencyIssues.Length == 0)
            return null;

        var severity = hasReadError ? AttentionSeverity.Critical : AttentionSeverity.Warning;
        var issueKeys = new List<string>();
        if (hasReadError)
            issueKeys.Add("manifest-read-error");
        issueKeys.AddRange(dependencyIssues.Select(issue =>
            $"dependency:{issue.Dependency.Id}:{(issue.Found is null ? "missing" : "disabled")}"));

        var summary = BuildSummary(hasReadError, dependencyIssues);
        return new(
            $"feature:{feature.Id}",
            HashGeneration(feature.Id, issueKeys),
            severity,
            hasReadError ? $"Module metadata failed for {feature.DisplayName}" : $"Module dependencies are unavailable for {feature.DisplayName}",
            summary,
            observedAt,
            observedAt,
            issueKeys.Count,
            new("/modules", "Inspect modules"),
            [new("module", feature.Id)],
            AttentionSensitivity.Metadata);
    }

    private static string BuildSummary(
        bool hasReadError,
        IReadOnlyCollection<(FeatureDependency Dependency, FeatureCatalogItem? Found)> dependencyIssues)
    {
        var parts = new List<string>();
        if (hasReadError)
            parts.Add("The module manifest could not be read");
        if (dependencyIssues.Count > 0)
        {
            var missing = dependencyIssues
                .Select(issue => $"{issue.Dependency.Id} ({(issue.Found is null ? "missing" : "disabled")})");
            parts.Add($"Required dependencies: {string.Join(", ", missing)}");
        }

        return string.Join(". ", parts) + ".";
    }

    private static string HashGeneration(string featureId, IEnumerable<string> issueKeys)
    {
        var canonical = featureId + "\n" + string.Join("\n", issueKeys.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
    }
}
