# Data Model: Elsa Studio UI System

## StudioUiToken

Represents a named visual decision consumed by shared primitives and modules.

- `name`: Stable token name.
- `category`: Typography, color, spacing, radius, border, shadow, focus, status,
  or motion.
- `lightValue`: Value used in light theme.
- `darkValue`: Value used in dark theme.
- `usage`: Allowed usage description.
- `deprecatedBy`: Optional replacement token.

## StudioUiPrimitive

Represents a reusable component or pattern.

- `id`: Stable primitive identifier.
- `name`: Human-readable component name.
- `category`: Shell, layout, data, inspector, form, feedback, diagnostics, or
  navigation.
- `props`: Supported public inputs.
- `states`: Loading, disabled, selected, active, empty, error, dirty, focused,
  and validation states as relevant.
- `accessibility`: Required labels, roles, keyboard behavior, and focus rules.
- `tokenUsage`: Tokens the primitive consumes.

## PageArchetype

Represents a reusable page composition available to modules.

- `id`: `resource-index`, `master-detail`, `configuration`, `overview`,
  `diagnostics-logs`, or specialized extension.
- `requiredPrimitives`: Primitives needed for the archetype.
- `optionalPrimitives`: Primitives available for richer workflows.
- `layoutRules`: Density, spacing, and responsive behavior.
- `antiPatterns`: Patterns that should not be used for this archetype.

## FeatureCatalogItem

Represents one configurable feature in the Feature Management screen.

- `id`: Stable feature identifier.
- `displayName`: User-facing name.
- `description`: Optional explanation.
- `sourceKind`: Built-in, package, local, or other source classification.
- `packageId` / `packageVersion`: Optional package metadata.
- `enabled`: Current enabled state.
- `advanced` / `experimental`: Flags that affect filtering and warnings.
- `settings`: Contributed setting descriptors.
- `configuration`: Draft configuration values.
- `readError`: Optional manifest/read error.

## StudioModuleRegistryItem

Represents one frontend or backend module in Module Management.

- `id`: Stable module identifier.
- `displayName`: User-facing name.
- `sourceKind`: Built-in, backend, frontend, package, local development, or
  disabled.
- `version`: Module version when available.
- `sdkVersion`: SDK version expected or used by the module.
- `compatibility`: Compatible, warning, incompatible, or unknown.
- `status`: Loaded, available, disabled, failed, incompatible, or pending.
- `scope`: Frontend, backend, or full-stack.
- `manifest`: Manifest path, hash, and loaded metadata.
- `contributions`: Navigation, routes, panels, widgets, setting editors, and
  endpoints.
- `diagnostics`: Load events, warnings, errors, and remediation hints.

## ModuleContribution

Represents one contribution made by a module.

- `type`: Navigation, route, panel, widget, setting editor, endpoint, diagnostic,
  or service.
- `id`: Contribution identifier.
- `label`: Optional user-facing label.
- `path`: Route or endpoint path when applicable.
- `order`: Optional ordering value.
- `status`: Active, disabled, failed, or unknown.
- `diagnostics`: Optional contribution-specific messages.
