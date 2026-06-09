using Elsa.Activities.Design.Core.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US6 — Read-contract surface pinning. Under the Model X reconciliation policy (Unit C
/// 2026-05-28), the parent <see cref="IActivityDefinition"/> exposes identity + creation
/// provenance + display only; per-pass mutating reconciliation state (LastSeenAt, IsStale,
/// RemovedAt, etc.) does NOT exist anywhere in the domain because reconciliation is one-shot
/// at creation time. The Version's content hash lives on
/// <see cref="IActivityDefinitionVersion.ReconcilliationHash"/> as an immutable read-only
/// property. Reflection tests fail loudly if a future refactor reintroduces operational
/// per-pass fields onto these contracts.
/// </summary>
public sealed class ReadContractSurfaceTests
{
    /// <summary>
    /// Operational per-pass field names that MUST NOT appear on any catalog read contract
    /// under Model X. If a future refactor adds one of these to <see cref="IActivityDefinition"/>
    /// or <see cref="IActivityDefinitionVersion"/>, the reconciliation policy has silently
    /// drifted back toward the pre-Model-X model.
    /// </summary>
    private static readonly string[] ForbiddenPerPassFieldNames =
    [
        "LastSeenAt",
        "LastReconciledAt",
        "LastReconciledBy",
        "SourceVersion",
        "IsStale",
        "RemovedAt",
    ];

    [Fact]
    public void IActivityDefinition_DoesNotExposeAnyPerPassReconciliationField()
    {
        var properties = typeof(IActivityDefinition)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fieldName in ForbiddenPerPassFieldNames)
        {
            Assert.DoesNotContain(fieldName, properties);
        }
    }

    [Fact]
    public void IActivityDefinitionVersion_DoesNotExposeAnyPerPassReconciliationField()
    {
        // ProvisioningHash IS allowed on the Version (it's an immutable artefact, not a
        // per-pass mutating field). The forbidden list excludes it explicitly.
        var properties = typeof(IActivityDefinitionVersion)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var fieldName in ForbiddenPerPassFieldNames)
        {
            Assert.DoesNotContain(fieldName, properties);
        }
    }

    [Fact]
    public void IActivityDefinitionVersion_ExposesProvisioningHash()
    {
        // Model X depends on the hash being on the Version (immutable) so the duplicate
        // detection path can compare incoming hash to persisted hash without re-loading
        // the version's full content.
        var properties = typeof(IActivityDefinitionVersion)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(IActivityDefinitionVersion.Hash), properties);
    }

    [Fact]
    public void IActivityDefinitionVersion_Version_IsStringSemVer()
    {
        // FR-017: the read contract exposes the author-controlled semver as a string, never
        // the legacy int. A regression that re-types it would break semver ordering and the
        // assembly-attribute source.
        var versionProperty = typeof(IActivityDefinitionVersion)
            .GetProperty(nameof(IActivityDefinitionVersion.Version));

        Assert.NotNull(versionProperty);
        Assert.Equal(typeof(string), versionProperty!.PropertyType);
    }

    [Fact]
    public void IActivityDefinition_DoesNotExposeIsBrowsable()
    {
        // IsBrowsable was removed entirely (catalog presence is the picker rule, not a
        // mutable flag). This test pins that removal — a regression that re-adds it would
        // fail here.
        var properties = typeof(IActivityDefinition)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("IsBrowsable", properties);
    }
}
