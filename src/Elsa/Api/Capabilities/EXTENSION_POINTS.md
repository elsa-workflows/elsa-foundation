# Extension points — API Capabilities

This module has one add-don't-replace contribution seam and one replaceable catalog service. Shared meanings
remain in the [feature specification](../../../../specs/092-domain-owned-apis/spec.md).

## Contributor seam

### `IApiCapabilitySource`

- **Kind:** typed Source, add-don't-replace.
- **Scope:** resolved and evaluated inside the active shell.
- **Purpose:** contribute a capability or link whose availability depends on operational services rather than
  merely on feature presence.
- **Registration:** `services.AddApiCapabilitySource<TSource>()`.
- **Output:** explicit `ApiCapabilityDeclaration` values. A Source must use stable IDs and links; it must not
  derive public contracts from its own type name, a feature name, or endpoint reflection.
- **Failure:** cancellation propagates. Provider failures and incompatible duplicate declarations fail document
  assembly; the catalog never silently advertises a partial or arbitrarily selected contract.

The `CollectingApiCapabilities` inline contribution event is available for framework modules that already use
the event contribution pattern. Handlers append the same declaration type and obey the same duplicate rules.

## Replaceable service

### `IApiCapabilityCatalog`

The default `ApiCapabilityCatalog` merges static declarations, typed Sources, and inline event contributions;
validates duplicates; and produces deterministic public views. A replacement must preserve:

- one capability per stable ID;
- positive major versions and unique link relations;
- shell-relative links;
- ordinal capability/link ordering;
- caller- and permission-neutral output;
- explicit conflict diagnostics rather than last-registration-wins behavior;
- reevaluation of operational Sources without feature-name inference.

Register a replacement before `ApiCapabilitiesFeature`, or replace the scoped
`IApiCapabilityCatalog` descriptor explicitly.

## Static declarations

Active domain API features call `AddApiCapability` during shell composition. Equivalent declarations are
idempotent; incompatible declarations throw `ApiCapabilityConflictException` immediately. Static declarations
are a feature-owned public-contract statement, not an independent provider extension point.

## References

- [Module README](README.md)
- [Management API OpenAPI contract](../../../../specs/092-domain-owned-apis/contracts/management-api.openapi.yaml)
- [Repository extension-point index](../../../../EXTENSION_POINTS.md)
