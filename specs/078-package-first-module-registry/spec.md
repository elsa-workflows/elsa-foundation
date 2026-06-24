# Feature Specification: Package-First Module Registry

## Summary

The module-management registry represents modules as packages. Each module row is
backed by a package identity and exposes the package's features as child rows.
The registry no longer treats individual shell features as top-level modules.

## Requirements

- `/_elsa/module-management/registry` MUST return `Modules` as package modules.
- Each module MUST include package identity, source, install path, manifest,
  dependencies, child features, and diagnostics.
- Feature child rows MUST include feature identity, display metadata, source
  kind, enabled state, configuration, settings, manifest path/hash, and
  diagnostics.
- Features with no package identity MUST be grouped under a synthetic
  `Elsa.Server` module.
- Package manifest read errors MUST be reported as package module diagnostics.
- Manifest-declared package identity mismatches MUST be reported as warnings.
- Package upload, reconcile, prune, feed, and retention operations remain
  unchanged.

## Current Package Source

The current implementation uses Nuplane active packages as the authoritative
package source. When an `Elsa.Platform.Catalog` package enumeration service is
available in this repo, it should replace the Nuplane-specific package source
behind the registry-building seam without changing the registry response shape.
