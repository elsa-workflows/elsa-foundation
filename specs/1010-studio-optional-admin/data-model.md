# Data model

Reuse FeatureCatalogResponse/FeatureApplyRequest/FeatureApplyResult, ShellFeatureConfigurationSnapshot,
FeatureConfigurationChange and FeatureCatalogRevisionConflictException from Modularity.Core. Store revisions
are keyed hashes of raw selected feature state; secret-bearing data never becomes a raw digest projection.

Installed addon settings contain a resolved management file and actual selected directory-feed identity/path.
Hosting supplies the installed shell/file; custom source replacement makes file mutation read-only. These
choices require restart. Management-file JSON preserves unrelated properties while feeds/retention are edited.

The supported Studio retention record has nullable KeepNewestInactiveVersionsPerPackage, an explicit legacy/
unsupported diagnostic and supported-policy state. Null means retain all, zero is intentional. It does not alias
Nuplane's legacy cleanup policy or introduce automatic deletion.

Reuse upstream PackageStorePruneRequest/Confirmation/RetentionPolicy and maintenance result data where possible.
A browser preview is transient state; a host/policy change clears it. Confirmation carries root label, physical
identity, enrollment epoch and exact policy; execution replans protection under ownership.

Bounded content holds a response permit, admitted install-path owner/borrow, normalized relative path, observed
length and detached bytes. Package ownership ends after detachment; response ownership ends after execution.
No new persisted entities, loader registry or roster model is introduced.
